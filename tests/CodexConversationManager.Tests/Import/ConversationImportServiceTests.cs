using System.Text.Json.Nodes;
using CodexConversationManager.Core.Import;
using CodexConversationManager.Core.LocalData;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexConversationManager.Tests.Import;

public sealed class ConversationImportServiceTests : IDisposable
{
    private const string SourceId = "11111111-1111-7111-8111-111111111111";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CodexConversationManagerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Apply_copies_rollout_registers_thread_and_assigns_existing_project()
    {
        var paths = await CreateCodexRootAsync();
        var source = await WriteSourceAsync();
        var preview = await PreviewAsync(source, paths, "daily");

        var result = await new ConversationImportService(paths, Path.Combine(_root, "backups"))
            .ApplyAsync(new ConversationImportRequest(preview, new ExistingProjectDestination("daily"), ImportProviderMode.CurrentLogin));

        Assert.True(File.Exists(result.ImportedFiles.Single()));
        Assert.True(await ThreadExistsAsync(paths.StateDatabase, SourceId));
        Assert.Equal("daily", await ReadProjectAssignmentAsync(paths.GlobalState, SourceId));
        var global = JsonNode.Parse(await File.ReadAllTextAsync(paths.GlobalState))!.AsObject();
        Assert.Equal("local", global["thread-project-assignments"]?[SourceId]?["projectKind"]?.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(paths.Root, "sqlite", "codex-dev.db")));
    }

    [Fact]
    public async Task Apply_creates_requested_directory_and_registers_new_project()
    {
        var paths = await CreateCodexRootAsync();
        var source = await WriteSourceAsync();
        var preview = await PreviewAsync(source, paths, null);
        var parent = Path.Combine(_root, "projects");

        await new ConversationImportService(paths, Path.Combine(_root, "backups"))
            .ApplyAsync(new ConversationImportRequest(preview, new NewProjectDestination(parent, "Imported"), ImportProviderMode.CurrentLogin));

        var projectRoot = Path.Combine(parent, "Imported");
        Assert.True(Directory.Exists(projectRoot));
        var global = JsonNode.Parse(await File.ReadAllTextAsync(paths.GlobalState))!.AsObject();
        var project = Assert.Single(global["local-projects"]!.AsObject().Select(property => property.Value!.AsObject()),
            value => value["name"]?.GetValue<string>() == "Imported");
        Assert.Equal("Imported", project["name"]!.GetValue<string>());
        Assert.Equal(projectRoot, project["rootPaths"]!.AsArray()[0]!.GetValue<string>());
    }

    [Fact]
    public async Task Apply_preserves_canonical_timestamp_in_imported_rollout_filename()
    {
        var paths = await CreateCodexRootAsync();
        var source = await WriteSourceAsync("rollout-2026-08-28T10-39-29-11111111-1111-7111-8111-111111111111.jsonl");
        var preview = await PreviewAsync(source, paths, "daily");

        var result = await new ConversationImportService(paths, Path.Combine(_root, "backups"))
            .ApplyAsync(new ConversationImportRequest(preview, new ExistingProjectDestination("daily"), ImportProviderMode.CurrentLogin));

        var imported = Assert.Single(result.ImportedFiles);
        Assert.Equal("rollout-2026-08-28T10-39-29-11111111-1111-7111-8111-111111111111.jsonl", Path.GetFileName(imported));
        Assert.Equal(Path.Combine(paths.Sessions, "2026", "08", "28"), Path.GetDirectoryName(imported));
    }

    [Fact]
    public async Task Apply_converts_paginated_history_to_local_legacy_history()
    {
        var paths = await CreateCodexRootAsync();
        var source = await WriteSourceAsync(historyMode: "paginated");
        var preview = await PreviewAsync(source, paths, "daily");

        var result = await new ConversationImportService(paths, Path.Combine(_root, "backups"))
            .ApplyAsync(new ConversationImportRequest(preview, new ExistingProjectDestination("daily"), ImportProviderMode.CurrentLogin));

        var metadata = JsonNode.Parse((await File.ReadAllLinesAsync(result.ImportedFiles.Single()))[0])!.AsObject();
        Assert.Equal("legacy", metadata["payload"]?["history_mode"]?.GetValue<string>());
    }

    [Fact]
    public async Task Apply_converts_item_completed_messages_to_legacy_events()
    {
        var paths = await CreateCodexRootAsync();
        var source = await WriteModernSourceAsync();
        var preview = await PreviewAsync(source, paths, "daily");

        var result = await new ConversationImportService(paths, Path.Combine(_root, "backups"))
            .ApplyAsync(new ConversationImportRequest(preview, new ExistingProjectDestination("daily"), ImportProviderMode.CurrentLogin));

        var records = (await File.ReadAllLinesAsync(result.ImportedFiles.Single()))
            .Select(line => JsonNode.Parse(line)!.AsObject()).ToList();
        Assert.Contains(records, record => record["type"]?.GetValue<string>() == "event_msg" &&
            record["payload"]?["type"]?.GetValue<string>() == "user_message" &&
            record["payload"]?["message"]?.GetValue<string>() == "hello from modern");
        Assert.Contains(records, record => record["type"]?.GetValue<string>() == "event_msg" &&
            record["payload"]?["type"]?.GetValue<string>() == "agent_message" &&
            record["payload"]?["message"]?.GetValue<string>() == "hello from assistant");
    }

    [Fact]
    public async Task Apply_restores_state_and_does_not_leave_rollout_when_global_state_write_fails()
    {
        var paths = await CreateCodexRootAsync();
        var source = await WriteSourceAsync();
        var preview = await PreviewAsync(source, paths, "daily");
        var beforeState = await File.ReadAllTextAsync(paths.StateDatabase);
        var beforeGlobal = await File.ReadAllTextAsync(paths.GlobalState);
        var blockedGlobal = paths.GlobalState + ".blocked";
        File.Move(paths.GlobalState, blockedGlobal);

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => new ConversationImportService(paths, Path.Combine(_root, "backups"))
                .ApplyAsync(new ConversationImportRequest(preview, new ExistingProjectDestination("daily"), ImportProviderMode.CurrentLogin)));
        }
        finally
        {
            File.Move(blockedGlobal, paths.GlobalState);
        }

        Assert.Equal(beforeState, await File.ReadAllTextAsync(paths.StateDatabase));
        Assert.Equal(beforeGlobal, await File.ReadAllTextAsync(paths.GlobalState));
        Assert.False(Directory.EnumerateFiles(paths.Sessions, "*.jsonl", SearchOption.AllDirectories).Any());
    }

    private async Task<CodexPaths> CreateCodexRootAsync()
    {
        Directory.CreateDirectory(_root);
        var paths = CodexPaths.FromRoot(_root);
        Directory.CreateDirectory(paths.Sessions);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.CatalogDatabase)!);
        await File.WriteAllTextAsync(paths.GlobalState, new JsonObject
        {
            ["project-order"] = new JsonArray("daily"),
            ["local-projects"] = new JsonObject
            {
                ["daily"] = new JsonObject { ["id"] = "daily", ["name"] = "日常对话", ["rootPaths"] = new JsonArray("D:\\AI\\daily") }
            },
            ["thread-project-assignments"] = new JsonObject(),
            ["sidebar-project-thread-orders"] = new JsonObject(),
            ["projectless-thread-ids"] = new JsonArray()
        }.ToJsonString());
        await CreateDatabasesAsync(paths);
        return paths;
    }

    private async Task CreateDatabasesAsync(CodexPaths paths)
    {
        await using (var connection = new SqliteConnection($"Data Source={paths.StateDatabase};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE threads (
                    id TEXT PRIMARY KEY, rollout_path TEXT NOT NULL, created_at INTEGER NOT NULL,
                    updated_at INTEGER NOT NULL, source TEXT NOT NULL, model_provider TEXT NOT NULL,
                    cwd TEXT NOT NULL, title TEXT NOT NULL, sandbox_policy TEXT NOT NULL,
                    approval_mode TEXT NOT NULL, tokens_used INTEGER NOT NULL DEFAULT 0,
                    has_user_event INTEGER NOT NULL DEFAULT 0, archived INTEGER NOT NULL DEFAULT 0,
                    archived_at INTEGER, preview TEXT NOT NULL DEFAULT '', recency_at INTEGER NOT NULL DEFAULT 0,
                    created_at_ms INTEGER, updated_at_ms INTEGER, recency_at_ms INTEGER, thread_source TEXT,
                    name TEXT, is_pinned INTEGER NOT NULL DEFAULT 0
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var connection = new SqliteConnection($"Data Source={paths.CatalogDatabase};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE local_thread_catalog (
                    host_id TEXT NOT NULL, thread_id TEXT NOT NULL, display_title TEXT NOT NULL,
                    source_created_at REAL NOT NULL, source_updated_at REAL NOT NULL, cwd TEXT NOT NULL,
                    source_kind TEXT NOT NULL, source_detail TEXT, model_provider TEXT NOT NULL,
                    git_branch TEXT, observation_sequence INTEGER NOT NULL, missing_candidate INTEGER NOT NULL DEFAULT 0,
                    thread_source TEXT, source_recency_at REAL NOT NULL DEFAULT 0, pending_observed_title INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY(host_id, thread_id)
                );
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task<string> WriteSourceAsync(string? fileName = null, string? historyMode = null)
    {
        var path = Path.Combine(_root, fileName ?? "source.jsonl");
        var metadata = new JsonObject
        {
            ["type"] = "session_meta",
            ["payload"] = new JsonObject
            {
                ["id"] = SourceId, ["timestamp"] = "2026-08-18T00:00:00Z", ["cwd"] = "D:\\AI\\daily",
                ["source"] = "cli", ["model_provider"] = "openai", ["title"] = "Imported"
            }
        };
        if (historyMode is not null) metadata["payload"]!["history_mode"] = historyMode;
        await File.WriteAllTextAsync(path, metadata.ToJsonString() + Environment.NewLine + "{\"type\":\"event_msg\",\"payload\":{\"thread_id\":\"" + SourceId + "\"}}\n");
        return path;
    }

    private async Task<string> WriteModernSourceAsync()
    {
        var path = Path.Combine(_root, "modern.jsonl");
        var lines = new[]
        {
            new JsonObject
            {
                ["type"] = "session_meta",
                ["payload"] = new JsonObject
                {
                    ["id"] = SourceId, ["timestamp"] = "2026-08-18T00:00:00Z", ["cwd"] = "D:\\AI\\daily",
                    ["source"] = "cli", ["model_provider"] = "openai", ["title"] = "Modern", ["history_mode"] = "paginated"
                }
            },
            new JsonObject
            {
                ["type"] = "event_msg", ["payload"] = new JsonObject
                {
                    ["type"] = "item_completed", ["thread_id"] = SourceId, ["turn_id"] = "turn-1",
                    ["item"] = new JsonObject { ["type"] = "UserMessage", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "hello from modern" }) }
                }
            },
            new JsonObject
            {
                ["type"] = "event_msg", ["payload"] = new JsonObject
                {
                    ["type"] = "item_completed", ["thread_id"] = SourceId, ["turn_id"] = "turn-1",
                    ["item"] = new JsonObject { ["type"] = "AgentMessage", ["content"] = new JsonArray(new JsonObject { ["type"] = "Text", ["text"] = "hello from assistant" }) }
                }
            }
        };
        await File.WriteAllLinesAsync(path, lines.Select(line => line.ToJsonString()));
        return path;
    }

    private static async Task<ConversationImportPreview> PreviewAsync(string source, CodexPaths paths, string? project)
    {
        var ids = new HashSet<string>();
        return await new ConversationImportPreviewService().PreviewAsync([source], "openai", ids, DuplicateIdResolution.Reject);
    }

    private static async Task<bool> ThreadExistsAsync(string path, string id)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM threads WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<string?> ReadProjectAssignmentAsync(string path, string id)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        return root["thread-project-assignments"]?[id]?["projectId"]?.GetValue<string>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
