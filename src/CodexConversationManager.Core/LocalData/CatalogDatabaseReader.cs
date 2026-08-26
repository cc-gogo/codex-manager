using Microsoft.Data.Sqlite;

namespace CodexConversationManager.Core.LocalData;

public sealed class CatalogDatabaseReader : ICatalogEvidenceSource
{
    public CatalogDatabaseReader(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
    }

    public string ConnectionString { get; }

    public async Task<IReadOnlyList<CatalogThreadEvidence>> ReadCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT thread_id, host_id, display_title, source_kind, thread_source, cwd,
                   missing_candidate, source_created_at, source_updated_at
            FROM local_thread_catalog
            ORDER BY thread_id, host_id
            """;

        var rows = new List<CatalogThreadEvidence>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new CatalogThreadEvidence(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6) != 0,
                FromUnixSeconds(reader.GetDouble(7)),
                FromUnixSeconds(reader.GetDouble(8))));
        }

        return rows;
    }

    private static DateTimeOffset FromUnixSeconds(double seconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(checked((long)Math.Round(seconds * 1000)));
}
