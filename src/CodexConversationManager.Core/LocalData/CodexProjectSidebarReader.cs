using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace CodexConversationManager.Core.LocalData;

public sealed class CodexProjectSidebarReader(string path, string? stateDatabasePath = null) : ICodexProjectSidebarProvider
{
    public async Task<CodexProjectSidebarSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject;
        if (root is null)
        {
            return CodexProjectSidebarSnapshot.Empty;
        }

        var order = (root["project-order"] as JsonArray ?? [])
            .Select(Value).Where(value => value is not null).Cast<string>().ToList();
        var orderById = order.Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index, StringComparer.OrdinalIgnoreCase);
        var projects = new List<CodexProject>();
        if (root["local-projects"] is JsonObject localProjects)
        {
            foreach (var property in localProjects)
            {
                if (property.Value is not JsonObject project || string.IsNullOrWhiteSpace(Value(project["name"])))
                {
                    continue;
                }

                var id = Value(project["id"]) ?? property.Key;
                var roots = (project["rootPaths"] as JsonArray ?? [])
                    .Select(Value).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToList();
                projects.Add(new CodexProject(id, Value(project["name"])!, roots,
                    orderById.TryGetValue(id, out var index) ? index : int.MaxValue));
            }
        }

        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root["thread-project-assignments"] is JsonObject assignmentObject)
        {
            foreach (var property in assignmentObject)
            {
                if (Guid.TryParseExact(property.Key, "D", out _) && property.Value is JsonObject assignment &&
                    Value(assignment["projectId"]) is { Length: > 0 } projectId)
                {
                    assignments[property.Key] = projectId;
                }
            }
        }

        var sidebarOrders = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (root["sidebar-project-thread-orders"] is JsonObject sidebarObject)
        {
            foreach (var property in sidebarObject)
            {
                if (property.Value is JsonObject item && item["threadIds"] is JsonArray ids)
                {
                    sidebarOrders[property.Key] = ids.Select(Value).Where(value => value is not null).Cast<string>().ToList();
                }
            }
        }

        var projectlessThreadIds = (root["projectless-thread-ids"] as JsonArray ?? [])
            .Select(Value)
            .Where(id => id is not null && Guid.TryParseExact(id, "D", out _))
            .Cast<string>()
            .ToList();
        var recentThreadIds = await ReadRecentThreadIdsAsync(false, cancellationToken).ConfigureAwait(false);
        var archivedRecentThreadIds = await ReadRecentThreadIdsAsync(true, cancellationToken).ConfigureAwait(false);

        return new CodexProjectSidebarSnapshot(
            projects.OrderBy(project => project.Order).ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            assignments,
            sidebarOrders,
            projectlessThreadIds,
            recentThreadIds)
        {
            ArchivedRecentThreadIds = archivedRecentThreadIds
        };
    }

    private async Task<IReadOnlyList<string>> ReadRecentThreadIdsAsync(bool archived, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stateDatabasePath) || !File.Exists(stateDatabasePath))
        {
            return [];
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(stateDatabasePath),
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var columns = await ReadColumnNamesAsync(connection, cancellationToken).ConfigureAwait(false);
        var recencyExpression = columns.Contains("recency_at_ms")
            ? "COALESCE(recency_at_ms, recency_at * 1000, 0)"
            : columns.Contains("recency_at")
                ? "COALESCE(recency_at * 1000, 0)"
                : "0";
        var subagentFilter = !archived && columns.Contains("thread_source")
            ? "AND COALESCE(thread_source, '') <> 'subagent'"
            : string.Empty;
        var limit = archived ? "LIMIT 6" : string.Empty;
        var sql = $"""
            SELECT id
            FROM threads
            WHERE archived = $archived
              {subagentFilter}
            ORDER BY {recencyExpression} DESC, id
            {limit}
            """;
        var ids = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$archived", archived ? 1 : 0);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            if (Guid.TryParseExact(id, "D", out _)) ids.Add(id);
        }

        return ids;
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

    private static string? Value(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
