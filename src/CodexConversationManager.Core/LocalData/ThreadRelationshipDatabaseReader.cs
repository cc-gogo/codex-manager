using Microsoft.Data.Sqlite;

namespace CodexConversationManager.Core.LocalData;

public sealed class ThreadRelationshipDatabaseReader(string databasePath) : IThreadRelationshipEvidenceSource
{
    public async Task<IReadOnlyList<ThreadRelationshipEvidence>> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath)) return [];

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT parent_thread_id, child_thread_id
            FROM thread_spawn_edges
            WHERE EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'thread_spawn_edges')
            """;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var edges = new List<ThreadRelationshipEvidence>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
                {
                    edges.Add(new ThreadRelationshipEvidence(reader.GetString(0), reader.GetString(1)));
                }
            }

            return edges;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            return [];
        }
    }
}
