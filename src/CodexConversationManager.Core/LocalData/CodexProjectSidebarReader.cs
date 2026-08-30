using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace CodexConversationManager.Core.LocalData;

public sealed class CodexProjectSidebarReader(string path, string? stateDatabasePath = null) : ICodexProjectSidebarProvider
{
    // Codex currently renders six projectless conversations in its Recent sidebar section.
    private const int SidebarRecentWindowSize = 6;

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
        var modern = await ReadModernSidebarAsync(cancellationToken).ConfigureAwait(false);
        if (modern.Projects.Count > 0)
        {
            projects = modern.Projects;
        }

        if (modern.ThreadProjectIds is not null)
        {
            foreach (var id in modern.ThreadIds)
            {
                assignments.Remove(id);
            }

            foreach (var (threadId, projectId) in modern.ThreadProjectIds)
            {
                assignments[threadId] = projectId;
            }
        }

        return new CodexProjectSidebarSnapshot(
            projects.OrderBy(project => project.Order).ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            assignments,
            sidebarOrders,
            projectlessThreadIds,
            modern.RecentThreadIds)
        {
            ArchivedRecentThreadIds = modern.ArchivedRecentThreadIds,
            PinnedThreadIds = modern.PinnedThreadIds,
            ThreadSectionIds = modern.ThreadSectionIds,
            ThreadSections = modern.ThreadSections
        };
    }

    private async Task<ModernSidebarState> ReadModernSidebarAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stateDatabasePath) || !File.Exists(stateDatabasePath))
        {
            return ModernSidebarState.Empty;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(stateDatabasePath),
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var columns = await ReadColumnNamesAsync(connection, "threads", cancellationToken).ConfigureAwait(false);
        if (columns.Count == 0)
        {
            return ModernSidebarState.Empty;
        }

        var projects = await ReadModernProjectsAsync(connection, cancellationToken).ConfigureAwait(false);
        var threadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? threadProjectIds = null;
        if (columns.Contains("project_id"))
        {
            threadProjectIds = await ReadModernProjectAssignmentsAsync(connection, threadIds, cancellationToken).ConfigureAwait(false);
        }

        var recentThreadIds = await ReadRecentThreadIdsAsync(connection, columns, false, cancellationToken).ConfigureAwait(false);
        var archivedRecentThreadIds = await ReadRecentThreadIdsAsync(connection, columns, true, cancellationToken).ConfigureAwait(false);
        var pinnedThreadIds = columns.Contains("is_pinned")
            ? await ReadThreadIdsAsync(connection, "COALESCE(is_pinned, 0) <> 0", cancellationToken).ConfigureAwait(false)
            : [];
        var threadSectionIds = columns.Contains("thread_section_id")
            ? await ReadThreadSectionsAsync(connection, cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var threadSections = await ReadModernSectionsAsync(connection, cancellationToken).ConfigureAwait(false);
        return new ModernSidebarState(projects, threadIds, threadProjectIds, recentThreadIds, archivedRecentThreadIds,
            pinnedThreadIds, threadSectionIds, threadSections);
    }

    private static async Task<List<CodexProject>> ReadModernProjectsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var projectColumns = await ReadColumnNamesAsync(connection, "projects", cancellationToken).ConfigureAwait(false);
        if (!projectColumns.Contains("id") || !projectColumns.Contains("name")) return [];

        var rootColumns = await ReadColumnNamesAsync(connection, "project_roots", cancellationToken).ConfigureAwait(false);
        var rootOrder = rootColumns.Contains("position") ? "position, path" : "path";
        var roots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (rootColumns.Contains("project_id") && rootColumns.Contains("path"))
        {
            await using var rootCommand = connection.CreateCommand();
            rootCommand.CommandText = $"SELECT project_id, path FROM project_roots ORDER BY {rootOrder}";
            await using var rootReader = await rootCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await rootReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (rootReader.IsDBNull(0) || rootReader.IsDBNull(1)) continue;
                var projectId = rootReader.GetString(0);
                var path = rootReader.GetString(1);
                if (!roots.TryGetValue(projectId, out var values)) roots[projectId] = values = [];
                values.Add(path);
            }
        }

        var projectOrder = projectColumns.Contains("position") ? "position, id" : "name, id";
        var projects = new List<CodexProject>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, name, {(projectColumns.Contains("position") ? "position" : "0")} FROM projects ORDER BY {projectOrder}";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
            var id = reader.GetString(0);
            var name = reader.GetString(1);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
            projects.Add(new CodexProject(id, name, roots.GetValueOrDefault(id) ?? [], reader.GetInt32(2)));
        }

        return projects;
    }

    private static async Task<Dictionary<string, string>> ReadModernProjectAssignmentsAsync(
        SqliteConnection connection,
        ISet<string> threadIds,
        CancellationToken cancellationToken)
    {
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, project_id FROM threads";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0)) continue;
            var id = reader.GetString(0);
            if (!Guid.TryParseExact(id, "D", out _)) continue;
            threadIds.Add(id);
            if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
            {
                assignments[id] = reader.GetString(1);
            }
        }

        return assignments;
    }

    private static async Task<IReadOnlyList<string>> ReadRecentThreadIdsAsync(
        SqliteConnection connection,
        IReadOnlySet<string> columns,
        bool archived,
        CancellationToken cancellationToken)
    {
        var recencyExpression = columns.Contains("recency_at_ms")
            ? columns.Contains("recency_at")
                ? "COALESCE(recency_at_ms, recency_at * 1000, 0)"
                : "COALESCE(recency_at_ms, 0)"
            : columns.Contains("recency_at")
                ? "COALESCE(recency_at * 1000, 0)"
                : "0";
        var previewFilter = columns.Contains("preview") ? "AND COALESCE(preview, '') <> ''" : string.Empty;
        var projectFilter = columns.Contains("project_id") ? "AND project_id IS NULL" : string.Empty;
        var sectionFilter = columns.Contains("thread_section_id") ? "AND thread_section_id IS NULL" : string.Empty;
        var pinnedFilter = columns.Contains("is_pinned") ? "AND COALESCE(is_pinned, 0) = 0" : string.Empty;
        var sql = $"""
            SELECT id
            FROM threads
            WHERE archived = $archived
              {previewFilter}
              {projectFilter}
              {sectionFilter}
              {pinnedFilter}
            ORDER BY {recencyExpression} DESC, id
            LIMIT {SidebarRecentWindowSize}
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

    private static async Task<IReadOnlyList<string>> ReadThreadIdsAsync(
        SqliteConnection connection,
        string condition,
        CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id FROM threads WHERE {condition} ORDER BY id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0) && Guid.TryParseExact(reader.GetString(0), "D", out _)) ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static async Task<Dictionary<string, string>> ReadThreadSectionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var memberships = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, thread_section_id FROM threads WHERE thread_section_id IS NOT NULL";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0) && !reader.IsDBNull(1) && Guid.TryParseExact(reader.GetString(0), "D", out _))
            {
                memberships[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return memberships;
    }

    private static async Task<IReadOnlyList<CodexThreadSection>> ReadModernSectionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var columns = await ReadColumnNamesAsync(connection, "thread_sections", cancellationToken).ConfigureAwait(false);
        if (!columns.Contains("id") || !columns.Contains("name")) return [];

        var sections = new List<CodexThreadSection>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name FROM thread_sections ORDER BY name, id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
            {
                sections.Add(new CodexThreadSection(reader.GetString(0), reader.GetString(1)));
            }
        }

        return sections;
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private sealed record ModernSidebarState(
        List<CodexProject> Projects,
        IReadOnlySet<string> ThreadIds,
        Dictionary<string, string>? ThreadProjectIds,
        IReadOnlyList<string> RecentThreadIds,
        IReadOnlyList<string> ArchivedRecentThreadIds,
        IReadOnlyList<string> PinnedThreadIds,
        IReadOnlyDictionary<string, string> ThreadSectionIds,
        IReadOnlyList<CodexThreadSection> ThreadSections)
    {
        public static ModernSidebarState Empty { get; } = new([], new HashSet<string>(StringComparer.OrdinalIgnoreCase), null, [], [], [], new Dictionary<string, string>(), []);
    }

    private static string? Value(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
