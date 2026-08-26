using Microsoft.Data.Sqlite;

namespace CodexConversationManager.Core.LocalData;

public sealed class StateDatabaseReader : IStateEvidenceSource
{
    public StateDatabaseReader(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
    }

    public string ConnectionString { get; }

    public async Task<IReadOnlyList<StateThreadEvidence>> ReadThreadsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var columns = await ReadColumnNamesAsync(connection, cancellationToken).ConfigureAwait(false);
        var recencyExpression = columns.Contains("recency_at_ms")
            ? "COALESCE(recency_at_ms, recency_at * 1000, 0)"
            : columns.Contains("recency_at")
                ? "COALESCE(recency_at * 1000, 0)"
                : "0";
        var sql = $"""
            SELECT id, rollout_path, source, thread_source, cwd, title, archived,
                   COALESCE(created_at_ms, created_at * 1000),
                   COALESCE(updated_at_ms, updated_at * 1000),
                   {recencyExpression}
            FROM threads
            ORDER BY id
            """;

        var rows = new List<StateThreadEvidence>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new StateThreadEvidence(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6) != 0,
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(8)),
                reader.GetInt64(9) > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(9))
                    : null));
        }

        return rows;
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(threads)";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }
}
