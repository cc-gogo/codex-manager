using System.Text.Json.Nodes;
using CodexConversationManager.Core.LocalData;
using Microsoft.Data.Sqlite;

namespace CodexConversationManager.Core.Sync;

public sealed class ProviderSyncService(CodexPaths paths, string configPath, string backupRoot)
{
    private const string SourceProviderLabel = "其他 provider";

    public async Task<ProviderSyncPlan> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var destination = await ReadConfiguredProviderAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return new ProviderSyncPlan(SourceProviderLabel, destination ?? string.Empty, []);
        }

        var targets = new List<ProviderSyncTarget>();
        foreach (var directory in new[] { paths.Sessions, paths.ArchivedSessions })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories))
            {
                var count = await CountRolloutAsync(file, destination, cancellationToken).ConfigureAwait(false);
                if (count > 0)
                {
                    targets.Add(new ProviderSyncTarget(file, "rollout", count));
                }
            }
        }

        foreach (var database in DatabaseSpecs())
        {
            var count = await CountAsync(database.Path, database.Table, destination, cancellationToken).ConfigureAwait(false);
            if (count > 0) targets.Add(new ProviderSyncTarget(database.Path, database.Table, count));
        }

        return new ProviderSyncPlan(SourceProviderLabel, destination, targets);
    }

    public async Task<ProviderSyncResult> ApplyAsync(ProviderSyncPlan plan, string? selectedBackupRoot = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plan.DestinationProvider) || plan.TotalCount == 0)
        {
            return new ProviderSyncResult(plan, string.Empty, 0);
        }

        var root = string.IsNullOrWhiteSpace(selectedBackupRoot) ? backupRoot : Path.GetFullPath(selectedBackupRoot);
        var backup = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        var backedUp = new List<(string Original, string Copy)>();
        try
        {
            foreach (var path in plan.Targets.Select(target => target.Path).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(paths.Root, path);
                var copy = Path.Combine(backup, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(path, copy, overwrite: true);
                backedUp.Add((path, copy));
            }

            var updated = 0;
            foreach (var target in plan.Targets.Where(target => target.Kind == "rollout"))
            {
                updated += await RewriteRolloutAsync(target.Path, plan.DestinationProvider, cancellationToken).ConfigureAwait(false);
            }

            foreach (var database in DatabaseSpecs())
            {
                if (!plan.Targets.Any(target => string.Equals(target.Path, database.Path, StringComparison.OrdinalIgnoreCase))) continue;
                updated += await UpdateDatabaseAsync(database.Path, database.Table, plan.DestinationProvider, cancellationToken).ConfigureAwait(false);
            }

            var remaining = await PreviewAsync(cancellationToken).ConfigureAwait(false);
            if (remaining.TotalCount != 0) throw new InvalidOperationException("Provider sync verification failed.");
            return new ProviderSyncResult(plan, backup, updated);
        }
        catch
        {
            foreach (var item in backedUp) File.Copy(item.Copy, item.Original, overwrite: true);
            throw;
        }
    }

    public async Task<string?> ReadConfiguredProviderAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath)) return null;
        var text = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var line = text.Split('\n').FirstOrDefault(value => value.TrimStart().StartsWith("model_provider", StringComparison.OrdinalIgnoreCase));
        if (line is null) return null;
        var equals = line.IndexOf('=');
        if (equals < 0) return null;
        var value = line[(equals + 1)..].Trim().TrimEnd(',');
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1].Trim();
        return value.Trim();
    }

    private static async Task<int> CountRolloutAsync(string path, string destination, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (JsonNode.Parse(line) is JsonObject value && value["type"]?.GetValue<string>() == "session_meta" &&
                !string.Equals(value["payload"]?["model_provider"]?.GetValue<string>(), destination, StringComparison.OrdinalIgnoreCase)) count++;
        }
        return count;
    }

    private static async Task<int> RewriteRolloutAsync(string path, string destination, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        var changed = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var value = JsonNode.Parse(lines[i]) as JsonObject;
            if (value?["type"]?.GetValue<string>() != "session_meta" || string.Equals(value["payload"]?["model_provider"]?.GetValue<string>(), destination, StringComparison.OrdinalIgnoreCase)) continue;
            value["payload"]!["model_provider"] = destination;
            lines[i] = value.ToJsonString();
            changed++;
        }
        if (changed > 0)
        {
            var temporary = path + ".provider-sync.tmp";
            await File.WriteAllLinesAsync(temporary, lines, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        return changed;
    }

    private static async Task<int> CountAsync(string path, string table, string destination, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return 0;
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!await HasColumnAsync(connection, table, "model_provider", cancellationToken).ConfigureAwait(false)) return 0;
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table} WHERE model_provider IS NOT NULL AND model_provider <> $destination";
        command.Parameters.AddWithValue("$destination", destination);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<int> UpdateDatabaseAsync(string path, string table, string destination, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!await HasColumnAsync(connection, table, "model_provider", cancellationToken).ConfigureAwait(false)) return 0;
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {table} SET model_provider = $destination WHERE model_provider IS NOT NULL AND model_provider <> $destination";
        command.Parameters.AddWithValue("$destination", destination);
        var count = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    private static async Task<bool> HasColumnAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private IEnumerable<(string Path, string Table)> DatabaseSpecs()
    {
        yield return (Path.Combine(paths.Root, "state_5.sqlite"), "threads");
        yield return (Path.Combine(paths.Root, "sqlite", "state_5.sqlite"), "threads");
        yield return (paths.CatalogDatabase, "local_thread_catalog");
    }
}
