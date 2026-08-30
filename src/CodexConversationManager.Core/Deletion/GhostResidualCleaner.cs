using CodexConversationManager.Core.LocalData;
using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;

namespace CodexConversationManager.Core.Deletion;

public sealed class GhostResidualCleaner(
    CodexPaths paths,
    GlobalStateIdRemover? globalStateRemover = null,
    TimeSpan? databaseLockTimeout = null) : IGhostResidualCleaner, ILocalThreadCleaner
{
    private readonly GlobalStateIdRemover _globalStateRemover = globalStateRemover ?? new GlobalStateIdRemover();
    private readonly ThreadDeletionBackupService _backupService = new();
    private readonly int _databaseLockTimeoutSeconds = Math.Max(1, (int)(databaseLockTimeout ?? TimeSpan.FromSeconds(1)).TotalSeconds);

    public async Task CleanupAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(id, "D", out _))
        {
            throw new ArgumentException("A full UUID is required.", nameof(id));
        }

        var bodies = await new SessionScanner(paths).ScanAsync(cancellationToken).ConfigureAwait(false);
        if (bodies.Any(body => string.Equals(body.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("A session body still exists; ghost cleanup is not allowed.");
        }

        await DeleteCatalogRowsAsync(id, cancellationToken).ConfigureAwait(false);
        await DeleteStateRowAsync(id, cancellationToken).ConfigureAwait(false);
        await DeleteHistoryRowsAsync(id, cancellationToken).ConfigureAwait(false);
        await RemoveGlobalStateReferencesAsync(id, cancellationToken).ConfigureAwait(false);
        await RemoveSessionIndexEntryAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteLocalThreadAsync(
        string id,
        IReadOnlyList<string> knownSessionPaths,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(id, "D", out _))
        {
            throw new ArgumentException("A full UUID is required.", nameof(id));
        }

        var sessionPaths = new List<string>();
        foreach (var path in knownSessionPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);
            if (!IsSessionPath(fullPath))
            {
                throw new InvalidOperationException("A local deletion can only remove known Codex session files.");
            }

            sessionPaths.Add(fullPath);
        }

        await using var backup = await _backupService.CreateAsync(paths, sessionPaths, cancellationToken).ConfigureAwait(false);
        try
        {
            // Database mutations succeed before any conversation body is removed.
            await DeleteCatalogRowsAsync(id, cancellationToken).ConfigureAwait(false);
            await DeleteStateRowAsync(id, cancellationToken).ConfigureAwait(false);
            await DeleteHistoryRowsAsync(id, cancellationToken).ConfigureAwait(false);
            await RemoveGlobalStateReferencesAsync(id, cancellationToken).ConfigureAwait(false);
            await RemoveSessionIndexEntryAsync(id, cancellationToken).ConfigureAwait(false);
            foreach (var sessionPath in sessionPaths.Where(File.Exists))
            {
                File.Delete(sessionPath);
            }
        }
        catch
        {
            try
            {
                await backup.RestoreAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // A process holding the database lock can also prevent replacing its backup.
                // Keep the original actionable lock error; the conversation body is untouched.
            }
            throw;
        }
    }

    private async Task RemoveGlobalStateReferencesAsync(string id, CancellationToken cancellationToken)
    {
        await _globalStateRemover.RemoveAsync(paths.GlobalState, id, cancellationToken).ConfigureAwait(false);
        await _globalStateRemover.RemoveAsync($"{paths.GlobalState}.bak", id, cancellationToken).ConfigureAwait(false);
    }

    private bool IsSessionPath(string path)
    {
        var sessions = Path.GetFullPath(paths.Sessions).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var archived = Path.GetFullPath(paths.ArchivedSessions).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) &&
               (path.StartsWith(sessions, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(archived, StringComparison.OrdinalIgnoreCase));
    }

    private async Task DeleteCatalogRowsAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.CatalogDatabase,
            Mode = SqliteOpenMode.ReadWrite,
            DefaultTimeout = _databaseLockTimeoutSeconds
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(connection, "BEGIN IMMEDIATE", cancellationToken).ConfigureAwait(false);
            await using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM local_thread_catalog WHERE thread_id = $id";
            delete.Parameters.AddWithValue("$id", id);
            var deleted = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (deleted > 0)
            {
                await ExecuteAsync(
                    connection,
                    "UPDATE local_thread_catalog_metadata SET catalog_revision = catalog_revision + 1 WHERE id = 1",
                    cancellationToken).ConfigureAwait(false);
            }

            await ExecuteAsync(connection, "COMMIT", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await TryRollbackAsync(connection).ConfigureAwait(false);
            throw ToActionableDatabaseException(exception);
        }
    }

    private async Task DeleteStateRowAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.StateDatabase,
            Mode = SqliteOpenMode.ReadWrite,
            DefaultTimeout = _databaseLockTimeoutSeconds
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(connection, "BEGIN IMMEDIATE", cancellationToken).ConfigureAwait(false);
            await using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM threads WHERE id = $id";
            delete.Parameters.AddWithValue("$id", id);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "COMMIT", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await TryRollbackAsync(connection).ConfigureAwait(false);
            throw ToActionableDatabaseException(exception);
        }
    }

    private async Task DeleteHistoryRowsAsync(string id, CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.ThreadHistoryDatabase))
        {
            return;
        }

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.ThreadHistoryDatabase,
            Mode = SqliteOpenMode.ReadWrite,
            DefaultTimeout = _databaseLockTimeoutSeconds
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(connection, "BEGIN IMMEDIATE", cancellationToken).ConfigureAwait(false);
            foreach (var table in new[]
                     {
                         "thread_turns",
                         "thread_items",
                         "thread_realtime_items",
                         "thread_history_projection_state"
                     })
            {
                if (!await TableExistsAsync(connection, table, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                await using var delete = connection.CreateCommand();
                delete.CommandText = $"DELETE FROM {table} WHERE thread_id = $id";
                delete.Parameters.AddWithValue("$id", id);
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await ExecuteAsync(connection, "COMMIT", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await TryRollbackAsync(connection).ConfigureAwait(false);
            throw ToActionableDatabaseException(exception);
        }
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $table LIMIT 1";
        command.Parameters.AddWithValue("$table", table);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private async Task RemoveSessionIndexEntryAsync(string id, CancellationToken cancellationToken)
    {
        var path = Path.Combine(paths.Root, "session_index.jsonl");
        if (!File.Exists(path))
        {
            return;
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        var retained = lines.Where(line => !IsIndexEntryForId(line, id)).ToArray();
        if (retained.Length == lines.Length)
        {
            return;
        }

        var temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllLinesAsync(temporaryPath, retained, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsIndexEntryForId(string line, string id)
    {
        try
        {
            return JsonNode.Parse(line) is JsonObject item &&
                   item["id"] is JsonValue value &&
                   value.TryGetValue<string>(out var entryId) &&
                   string.Equals(entryId, id, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task TryRollbackAsync(SqliteConnection connection)
    {
        try
        {
            await ExecuteAsync(connection, "ROLLBACK", CancellationToken.None).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            // BEGIN IMMEDIATE can fail before a transaction exists.
        }
    }

    private static Exception ToActionableDatabaseException(Exception exception) =>
        exception is SqliteException { SqliteErrorCode: 5 or 6 }
            ? new InvalidOperationException("Codex 正在占用数据库，请稍后重试。", exception)
            : exception;
}
