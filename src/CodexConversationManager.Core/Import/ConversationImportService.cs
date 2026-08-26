using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
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
            .Select(candidate => Path.Combine(paths.Sessions, $"rollout-{candidate.TargetId}.jsonl"))
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
                var target = Path.Combine(paths.Sessions, $"rollout-{candidate.TargetId}.jsonl");
                await WriteRolloutAsync(candidate, target, request.ProviderMode, cancellationToken).ConfigureAwait(false);
                createdFiles.Add(target);
                importedFiles.Add(target);
            }

            await InsertStateRowsAsync(request.Preview.Candidates, importedFiles, request.ProviderMode, cancellationToken).ConfigureAwait(false);
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
                if (providerMode == ImportProviderMode.CurrentLogin)
                    payload["model_provider"] = candidate.TargetProvider;
            }
            transformed.Add(node.ToJsonString());
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

    private async Task InsertStateRowsAsync(
        IReadOnlyList<ConversationImportCandidate> candidates,
        IReadOnlyList<string> importedFiles,
        ImportProviderMode providerMode,
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
            await InsertThreadAsync(connection, transaction, candidate, importedFiles[index], provider, cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (File.Exists(paths.CatalogDatabase))
            await InsertCatalogRowsAsync(candidates, providerMode, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertThreadAsync(SqliteConnection connection, SqliteTransaction transaction,
        ConversationImportCandidate candidate, string rolloutPath, string provider, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO threads
            (id, rollout_path, created_at, updated_at, source, model_provider, cwd, title,
             sandbox_policy, approval_mode, tokens_used, has_user_event, archived, preview,
             recency_at, created_at_ms, updated_at_ms, recency_at_ms, thread_source, name)
            VALUES ($id, $rollout, $created, $updated, $source, $provider, $cwd, $title,
                    $sandbox, $approval, 0, 1, 0, $preview, $recency,
                    $created_ms, $updated_ms, $recency_ms, 'user', $name)
            """;
        var created = candidate.CreatedAt.ToUnixTimeSeconds();
        var updated = candidate.UpdatedAt.ToUnixTimeSeconds();
        command.Parameters.AddWithValue("$id", candidate.TargetId);
        command.Parameters.AddWithValue("$rollout", rolloutPath);
        command.Parameters.AddWithValue("$created", created);
        command.Parameters.AddWithValue("$updated", updated);
        command.Parameters.AddWithValue("$source", "cli");
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$cwd", candidate.Cwd);
        command.Parameters.AddWithValue("$title", candidate.Title);
        command.Parameters.AddWithValue("$sandbox", "workspace-write");
        command.Parameters.AddWithValue("$approval", "on-request");
        command.Parameters.AddWithValue("$preview", candidate.Title);
        command.Parameters.AddWithValue("$recency", updated);
        command.Parameters.AddWithValue("$created_ms", candidate.CreatedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$updated_ms", candidate.UpdatedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$recency_ms", candidate.UpdatedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$name", candidate.Title);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
