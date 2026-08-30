using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using CodexConversationManager.Core.LocalData;
using Microsoft.Data.Sqlite;

namespace CodexConversationManager.Core.Import;

public abstract record ImportDestination;
public sealed record ExistingProjectDestination(string ProjectId) : ImportDestination;
public sealed record ProjectlessDestination : ImportDestination;
public sealed record NewProjectDestination(string ParentDirectory, string ProjectName) : ImportDestination;

public sealed record ConversationImportRequest(
    ConversationImportPreview Preview,
    ImportDestination Destination,
    ImportProviderMode ProviderMode);

public sealed record ConversationImportResult(
    IReadOnlyList<string> ImportedFiles,
    string BackupPath,
    int ImportedCount);

public sealed class ConversationImportService(CodexPaths paths, string backupRoot) : IConversationImportService
{
    public async Task<ConversationImportResult> ApplyAsync(
        ConversationImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Preview.Candidates.Count == 0) throw new InvalidOperationException("没有可导入的有效对话。");
        if (request.Preview.Issues.Count > 0) throw new InvalidOperationException("导入列表中存在未解决的问题，请先修正后再导入。");

        var destinationFiles = request.Preview.Candidates
            .Select(candidate => GetDestinationPath(candidate))
            .ToList();
        var backup = await ImportBackupService.CreateAsync(paths, destinationFiles, backupRoot, cancellationToken).ConfigureAwait(false);
        var createdFiles = new List<string>();
        var createdProjectDirectory = (string?)null;
        try
        {
            if (request.Destination is NewProjectDestination newProject)
            {
                var requestedDirectory = Path.Combine(Path.GetFullPath(newProject.ParentDirectory), newProject.ProjectName.Trim());
                if (Directory.Exists(requestedDirectory) || File.Exists(requestedDirectory))
                    throw new InvalidOperationException("新项目目录已存在，请选择其他名称。");
                createdProjectDirectory = requestedDirectory;
            }
            var projectId = await PrepareProjectAsync(request.Destination, cancellationToken).ConfigureAwait(false);

            var importedFiles = new List<string>();
            foreach (var candidate in request.Preview.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = GetDestinationPath(candidate);
                await WriteRolloutAsync(candidate, target, request.ProviderMode, cancellationToken).ConfigureAwait(false);
                createdFiles.Add(target);
                importedFiles.Add(target);
            }

            await InsertStateRowsAsync(request.Preview.Candidates, importedFiles, request.ProviderMode, projectId, cancellationToken).ConfigureAwait(false);
            await UpdateGlobalStateAsync(request.Preview.Candidates, projectId, request.Destination, cancellationToken).ConfigureAwait(false);
            await ValidateAsync(request.Preview.Candidates, importedFiles, projectId, cancellationToken).ConfigureAwait(false);
            return new ConversationImportResult(importedFiles, backup.Root, importedFiles.Count);
        }
        catch
        {
            await ImportBackupService.RestoreAsync(backup, createdFiles, cancellationToken).ConfigureAwait(false);
            if (createdProjectDirectory is not null && Directory.Exists(createdProjectDirectory) &&
                !Directory.EnumerateFileSystemEntries(createdProjectDirectory).Any())
                Directory.Delete(createdProjectDirectory);
            throw;
        }
    }

    private string GetDestinationPath(ConversationImportCandidate candidate)
    {
        var sourceName = Path.GetFileName(candidate.SourcePath);
        var match = Regex.Match(sourceName,
            @"^rollout-(?<stamp>\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2})-(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\.jsonl$",
            RegexOptions.CultureInvariant);
        var stamp = match.Success
            ? match.Groups["stamp"].Value
            : candidate.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd'T'HH-mm-ss");
        var fileName = $"rollout-{stamp}-{candidate.TargetId}.jsonl";
        var date = DateTime.ParseExact(stamp, "yyyy-MM-dd'T'HH-mm-ss", null);
        return Path.Combine(paths.Sessions, date.ToString("yyyy"), date.ToString("MM"), date.ToString("dd"), fileName);
    }

    private async Task<string?> PrepareProjectAsync(ImportDestination destination, CancellationToken cancellationToken)
    {
        if (destination is ExistingProjectDestination existing)
        {
            var existingRoot = await ReadGlobalStateAsync(cancellationToken).ConfigureAwait(false);
            if (existingRoot["local-projects"]?[existing.ProjectId] is not JsonObject)
                throw new InvalidOperationException("指定的项目不存在。");
            return existing.ProjectId;
        }

        if (destination is ProjectlessDestination) return null;
        var newProject = (NewProjectDestination)destination;
        ArgumentException.ThrowIfNullOrWhiteSpace(newProject.ParentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(newProject.ProjectName);
        var parent = Path.GetFullPath(newProject.ParentDirectory);
        var projectDirectory = Path.Combine(parent, newProject.ProjectName.Trim());
        Directory.CreateDirectory(projectDirectory);

        var root = await ReadGlobalStateAsync(cancellationToken).ConfigureAwait(false);
        var projectId = Guid.NewGuid().ToString("D");
        var projects = root["local-projects"] as JsonObject ?? new JsonObject();
        root["local-projects"] = projects;
        projects[projectId] = new JsonObject
        {
            ["id"] = projectId,
            ["name"] = newProject.ProjectName.Trim(),
            ["rootPaths"] = new JsonArray(projectDirectory)
        };
        var order = root["project-order"] as JsonArray ?? new JsonArray();
        root["project-order"] = order;
        order.Add(projectId);
        await WriteGlobalStateAsync(root, cancellationToken).ConfigureAwait(false);
        return projectId;
    }

    private async Task WriteRolloutAsync(
        ConversationImportCandidate candidate,
        string target,
        ImportProviderMode providerMode,
        CancellationToken cancellationToken)
    {
        var sourceLines = await File.ReadAllLinesAsync(candidate.SourcePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        var transformed = new List<string>(sourceLines.Length);
        foreach (var line in sourceLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var node = JsonNode.Parse(line) ?? throw new InvalidOperationException("导入文件包含空 JSON 记录。");
            if (candidate.HasDuplicateId) ReplaceExactStrings(node, candidate.SourceId, candidate.TargetId);
            if (node is JsonObject value && string.Equals(value["type"]?.GetValue<string>(), "session_meta", StringComparison.Ordinal) &&
                value["payload"] is JsonObject payload)
            {
                payload["id"] = candidate.TargetId;
                // Imported JSONL files have no paginated-history service behind them.
                // Use Codex's local rollout reader instead.
                if (string.Equals(payload["history_mode"]?.GetValue<string>(), "paginated", StringComparison.OrdinalIgnoreCase))
                    payload["history_mode"] = "legacy";
                if (providerMode == ImportProviderMode.CurrentLogin)
                    payload["model_provider"] = candidate.TargetProvider;
            }
            transformed.Add(node.ToJsonString());
            if (node is JsonObject modernRecord && TryCreateLegacyMessageEvent(modernRecord, out var legacyEvent))
                transformed.Add(legacyEvent.ToJsonString());
        }

        if (transformed.Count == 0) throw new InvalidOperationException("导入文件为空。");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".importing";
        await File.WriteAllLinesAsync(temporary, transformed, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        try
        {
            _ = JsonNode.Parse(transformed[0]);
            File.Move(temporary, target, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool TryCreateLegacyMessageEvent(JsonObject record, out JsonObject legacyEvent)
    {
        legacyEvent = null!;
        if (!string.Equals(record["type"]?.GetValue<string>(), "event_msg", StringComparison.Ordinal) ||
            record["payload"] is not JsonObject payload ||
            !string.Equals(payload["type"]?.GetValue<string>(), "item_completed", StringComparison.Ordinal))
            return false;

        using var document = JsonDocument.Parse(record.ToJsonString());
        if (!RolloutMessageExtractor.TryExtract(document.RootElement, out var message))
            return false;

        var legacyType = message.Role switch
        {
            "user" => "user_message",
            "assistant" => "agent_message",
            _ => null
        };
        if (legacyType is null)
            return false;

        var legacyPayload = new JsonObject
        {
            ["type"] = legacyType,
            ["message"] = message.Text
        };
        CopyString(payload, legacyPayload, "thread_id");
        CopyString(payload, legacyPayload, "turn_id");
        legacyEvent = new JsonObject
        {
            ["type"] = "event_msg",
            ["payload"] = legacyPayload
        };
        CopyString(record, legacyEvent, "timestamp");
        return true;
    }

    private static void CopyString(JsonObject source, JsonObject target, string property)
    {
        if (source[property]?.GetValue<string>() is { } value)
            target[property] = value;
    }

    private async Task InsertStateRowsAsync(
        IReadOnlyList<ConversationImportCandidate> candidates,
        IReadOnlyList<string> importedFiles,
        ImportProviderMode providerMode,
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.StateDatabase)) throw new InvalidOperationException("Codex state_5.sqlite 不存在。");
        await using var connection = new SqliteConnection($"Data Source={paths.StateDatabase};Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var provider = providerMode == ImportProviderMode.CurrentLogin ? candidate.TargetProvider : candidate.SourceProvider;
            await InsertThreadAsync(connection, transaction, candidate, importedFiles[index], provider, projectId, cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (File.Exists(paths.CatalogDatabase))
            await InsertCatalogRowsAsync(candidates, providerMode, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertThreadAsync(SqliteConnection connection, SqliteTransaction transaction,
        ConversationImportCandidate candidate, string rolloutPath, string provider, string? projectId, CancellationToken cancellationToken)
    {
        var columns = await ReadColumnsAsync(connection, transaction, "threads", cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<string, (string Parameter, object? Value)>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = ("$id", candidate.TargetId), ["rollout_path"] = ("$rollout", rolloutPath),
            ["created_at"] = ("$created", candidate.CreatedAt.ToUnixTimeSeconds()), ["updated_at"] = ("$updated", candidate.UpdatedAt.ToUnixTimeSeconds()),
            ["source"] = ("$source", "cli"), ["model_provider"] = ("$provider", provider), ["cwd"] = ("$cwd", candidate.Cwd),
            ["title"] = ("$title", candidate.Title), ["sandbox_policy"] = ("$sandbox", "workspace-write"), ["approval_mode"] = ("$approval", "on-request"),
            ["tokens_used"] = ("$tokens", 0), ["has_user_event"] = ("$has_user", 1), ["archived"] = ("$archived", 0),
            ["preview"] = ("$preview", candidate.Title), ["recency_at"] = ("$recency", candidate.UpdatedAt.ToUnixTimeSeconds()),
            ["created_at_ms"] = ("$created_ms", candidate.CreatedAt.ToUnixTimeMilliseconds()), ["updated_at_ms"] = ("$updated_ms", candidate.UpdatedAt.ToUnixTimeMilliseconds()),
            ["recency_at_ms"] = ("$recency_ms", candidate.UpdatedAt.ToUnixTimeMilliseconds()), ["thread_source"] = ("$thread_source", "user"), ["name"] = ("$name", candidate.Title),
            ["history_mode"] = ("$history_mode", "legacy"), ["project_id"] = ("$project_id", projectId), ["thread_section_id"] = ("$section", null), ["is_pinned"] = ("$pinned", 0)
        };
        var selected = values.Where(pair => columns.Contains(pair.Key)).ToList();
        if (!columns.Contains("id")) throw new InvalidOperationException("Codex threads 表缺少 id 列。");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO threads ({string.Join(", ", selected.Select(pair => pair.Key))}) VALUES ({string.Join(", ", selected.Select(pair => pair.Value.Parameter))})";
        foreach (var pair in selected) command.Parameters.AddWithValue(pair.Value.Parameter, pair.Value.Value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(SqliteConnection connection, SqliteTransaction transaction, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table})";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) columns.Add(reader.GetString(1));
        return columns;
    }

    private async Task InsertCatalogRowsAsync(IReadOnlyList<ConversationImportCandidate> candidates,
        ImportProviderMode providerMode, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={paths.CatalogDatabase};Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO local_thread_catalog
                (host_id, thread_id, display_title, source_created_at, source_updated_at, cwd,
                 source_kind, source_detail, model_provider, git_branch, observation_sequence,
                 missing_candidate, thread_source, source_recency_at, pending_observed_title)
                VALUES ('local', $id, $title, $created, $updated, $cwd, 'cli', NULL, $provider,
                        NULL, $sequence, 0, 'user', $recency, 0)
                """;
            command.Parameters.AddWithValue("$id", candidate.TargetId);
            command.Parameters.AddWithValue("$title", candidate.Title);
            command.Parameters.AddWithValue("$created", candidate.CreatedAt.ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$updated", candidate.UpdatedAt.ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$cwd", candidate.Cwd);
            command.Parameters.AddWithValue("$provider", providerMode == ImportProviderMode.CurrentLogin ? candidate.TargetProvider : candidate.SourceProvider);
            command.Parameters.AddWithValue("$sequence", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$recency", candidate.UpdatedAt.ToUnixTimeSeconds());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateGlobalStateAsync(IReadOnlyList<ConversationImportCandidate> candidates,
        string? projectId, ImportDestination destination, CancellationToken cancellationToken)
    {
        var root = await ReadGlobalStateAsync(cancellationToken).ConfigureAwait(false);
        var assignments = root["thread-project-assignments"] as JsonObject ?? new JsonObject();
        var projectOrders = root["sidebar-project-thread-orders"] as JsonObject ?? new JsonObject();
        var projectless = root["projectless-thread-ids"] as JsonArray ?? new JsonArray();
        root["thread-project-assignments"] = assignments;
        root["sidebar-project-thread-orders"] = projectOrders;
        root["projectless-thread-ids"] = projectless;
        var order = projectId is null ? null : projectOrders[projectId] as JsonObject ?? new JsonObject { ["threadIds"] = new JsonArray() };
        if (projectId is not null) projectOrders[projectId] = order!;
        var threadIds = projectId is null ? null : order!["threadIds"] as JsonArray ?? new JsonArray();
        if (projectId is not null) order!["threadIds"] = threadIds;
        foreach (var candidate in candidates)
        {
            if (projectId is null) assignments.Remove(candidate.TargetId);
            else assignments[candidate.TargetId] = new JsonObject
            {
                ["projectKind"] = "local",
                ["projectId"] = projectId
            };
            RemoveString(projectless, candidate.TargetId);
            if (threadIds is not null && !threadIds.Any(node => string.Equals(node?.GetValue<string>(), candidate.TargetId, StringComparison.OrdinalIgnoreCase)))
                threadIds.Add(candidate.TargetId);
            if (projectId is null) projectless.Add(candidate.TargetId);
        }
        await WriteGlobalStateAsync(root, cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateAsync(IReadOnlyList<ConversationImportCandidate> candidates,
        IReadOnlyList<string> importedFiles, string? projectId, CancellationToken cancellationToken)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            if (!File.Exists(importedFiles[index])) throw new InvalidOperationException("导入文件校验失败。");
            await using (var connection = new SqliteConnection($"Data Source={paths.StateDatabase};Mode=ReadOnly;Pooling=False"))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT count(*) FROM threads WHERE id = $id";
                command.Parameters.AddWithValue("$id", candidates[index].TargetId);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
                    throw new InvalidOperationException("Codex state 索引校验失败。");
            }
        }
        _ = projectId;
    }

    private async Task<JsonObject> ReadGlobalStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.GlobalState)) throw new InvalidOperationException("Codex 全局项目状态文件不存在。");
        var text = await File.ReadAllTextAsync(paths.GlobalState, cancellationToken).ConfigureAwait(false);
        return JsonNode.Parse(text)?.AsObject() ?? throw new InvalidOperationException("Codex 全局项目状态文件无效。");
    }

    private async Task WriteGlobalStateAsync(JsonObject root, CancellationToken cancellationToken)
    {
        var temporary = paths.GlobalState + ".importing";
        await File.WriteAllTextAsync(temporary, root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, paths.GlobalState, overwrite: true);
    }

    private static void ReplaceExactStrings(JsonNode node, string source, string target)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var text) && string.Equals(text, source, StringComparison.OrdinalIgnoreCase))
                    obj[property.Key] = target;
                else if (property.Value is not null) ReplaceExactStrings(property.Value, source, target);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue<string>(out var text) && string.Equals(text, source, StringComparison.OrdinalIgnoreCase))
                    array[index] = target;
                else if (array[index] is not null) ReplaceExactStrings(array[index]!, source, target);
            }
        }
    }

    private static void RemoveString(JsonArray array, string value)
    {
        for (var index = array.Count - 1; index >= 0; index--)
            if (string.Equals(array[index]?.GetValue<string>(), value, StringComparison.OrdinalIgnoreCase)) array.RemoveAt(index);
    }
}
