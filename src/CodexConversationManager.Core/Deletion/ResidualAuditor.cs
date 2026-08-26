using CodexConversationManager.Core.LocalData;
using Microsoft.Data.Sqlite;

namespace CodexConversationManager.Core.Deletion;

public sealed class ResidualAuditor(CodexPaths paths) : IResidualAuditor
{
    public async Task<bool> HasResidualsAsync(string id, CancellationToken cancellationToken = default)
    {
        var bodies = await new SessionScanner(paths).ScanAsync(cancellationToken).ConfigureAwait(false);
        if (bodies.Any(body => string.Equals(body.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (await HasRowAsync(paths.StateDatabase, "threads", "id", id, cancellationToken).ConfigureAwait(false) ||
            await HasRowAsync(paths.CatalogDatabase, "local_thread_catalog", "thread_id", id, cancellationToken)
                .ConfigureAwait(false))
        {
            return true;
        }

        foreach (var globalStatePath in new[] { paths.GlobalState, $"{paths.GlobalState}.bak" })
        {
            if (!File.Exists(globalStatePath))
            {
                continue;
            }

            var references = await new GlobalStateReader(globalStatePath).ReadReferencesAsync(cancellationToken)
                .ConfigureAwait(false);
            if (references.Any(reference => string.Equals(reference.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return await HasSessionIndexEntryAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasSessionIndexEntryAsync(string id, CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(paths.Root, "session_index.jsonl");
        if (!File.Exists(indexPath))
        {
            return false;
        }

        await using var stream = new FileStream(indexPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Contains(id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> HasRowAsync(
        string database,
        string table,
        string column,
        string id,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(database))
        {
            return false;
        }

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT EXISTS(SELECT 1 FROM {table} WHERE {column} = $id)";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }
}
