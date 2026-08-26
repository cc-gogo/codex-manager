using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.LocalData;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexConversationManager.Tests.Deletion;

public sealed class GhostResidualCleanerTests
{
    private const string TargetId = "99999999-9999-7999-8999-999999999999";
    private const string OtherId = "88888888-8888-7888-8888-888888888888";

    [Fact]
    public async Task Cleanup_removes_only_exact_id_without_creating_backup()
    {
        var root = CreateFixture(includeTargetBody: false);
        try
        {
            var paths = CodexPaths.FromRoot(root);
            var cleaner = new GhostResidualCleaner(paths);

            await cleaner.CleanupAsync(TargetId);

            Assert.Equal([OtherId], await ReadIdsAsync(paths.StateDatabase, "threads", "id"));
            Assert.Equal([OtherId], await ReadIdsAsync(paths.CatalogDatabase, "local_thread_catalog", "thread_id"));
            Assert.Equal(8L, await ReadScalarAsync(paths.CatalogDatabase, "SELECT catalog_revision FROM local_thread_catalog_metadata WHERE id=1"));
            var global = JsonNode.Parse(await File.ReadAllTextAsync(paths.GlobalState))!;
            Assert.Null(global["threadTitles"]?[TargetId]);
            Assert.Equal(OtherId, global["recent"]?[0]?.GetValue<string>());
            Assert.Equal($"prefix-{TargetId}-suffix", global["recent"]?[1]?.GetValue<string>());
            var indexLines = await File.ReadAllLinesAsync(Path.Combine(root, "session_index.jsonl"));
            Assert.Single(indexLines);
            Assert.Contains(OtherId, indexLines[0]);
            Assert.Empty(Directory.EnumerateFiles(root, "*.bak", SearchOption.AllDirectories));
            Assert.False(await new ResidualAuditor(paths).HasResidualsAsync(TargetId));
        }
        finally
        {
            DeleteFixture(root);
        }
    }

    [Fact]
    public async Task Existing_body_blocks_all_database_writes()
    {
        var root = CreateFixture(includeTargetBody: true);
        try
        {
            var paths = CodexPaths.FromRoot(root);
            var beforeState = Hash(paths.StateDatabase);
            var beforeCatalog = Hash(paths.CatalogDatabase);
            var beforeGlobal = Hash(paths.GlobalState);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new GhostResidualCleaner(paths).CleanupAsync(TargetId));

            Assert.Equal(beforeState, Hash(paths.StateDatabase));
            Assert.Equal(beforeCatalog, Hash(paths.CatalogDatabase));
            Assert.Equal(beforeGlobal, Hash(paths.GlobalState));
        }
        finally
        {
            DeleteFixture(root);
        }
    }

    [Fact]
    public async Task DeleteLocalThread_removes_known_session_body_and_all_local_indexes()
    {
        var root = CreateFixture(includeTargetBody: true);
        try
        {
            var paths = CodexPaths.FromRoot(root);
            var bodyPath = Path.Combine(paths.Sessions, $"rollout-{TargetId}.jsonl");

            await new GhostResidualCleaner(paths).DeleteLocalThreadAsync(TargetId, [bodyPath]);

            Assert.False(File.Exists(bodyPath));
            Assert.Equal([OtherId], await ReadIdsAsync(paths.StateDatabase, "threads", "id"));
            Assert.Equal([OtherId], await ReadIdsAsync(paths.CatalogDatabase, "local_thread_catalog", "thread_id"));
            Assert.False(await new ResidualAuditor(paths).HasResidualsAsync(TargetId));
        }
        finally
        {
            DeleteFixture(root);
        }
    }

    [Fact]
    public async Task DeleteLocalThread_removes_the_target_from_primary_and_backup_global_state()
    {
        var root = CreateFixture(includeTargetBody: false);
        try
        {
            var paths = CodexPaths.FromRoot(root);
            var backupPath = $"{paths.GlobalState}.bak";
            await File.WriteAllTextAsync(backupPath, await File.ReadAllTextAsync(paths.GlobalState));

            await new GhostResidualCleaner(paths).DeleteLocalThreadAsync(TargetId, []);

            var primaryReferences = await new GlobalStateReader(paths.GlobalState).ReadReferencesAsync();
            var backupReferences = await new GlobalStateReader(backupPath).ReadReferencesAsync();
            Assert.DoesNotContain(primaryReferences, reference => reference.Id == TargetId);
            Assert.DoesNotContain(backupReferences, reference => reference.Id == TargetId);
            Assert.Contains(backupReferences, reference => reference.Id == OtherId);
        }
        finally
        {
            DeleteFixture(root);
        }
    }

    [Fact]
    public async Task DeleteLocalThread_reports_an_immediate_actionable_error_when_codex_holds_a_database_write_lock()
    {
        var root = CreateFixture(includeTargetBody: false);
        try
        {
            var paths = CodexPaths.FromRoot(root);
            await using var lockConnection = new SqliteConnection($"Data Source={paths.CatalogDatabase};Pooling=False");
            await lockConnection.OpenAsync();
            await using var lockCommand = lockConnection.CreateCommand();
            lockCommand.CommandText = "BEGIN IMMEDIATE";
            await lockCommand.ExecuteNonQueryAsync();

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new GhostResidualCleaner(paths, databaseLockTimeout: TimeSpan.Zero)
                    .DeleteLocalThreadAsync(TargetId, []));

            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
            Assert.Contains("Codex 正在占用数据库", exception.Message);
        }
        finally
        {
            DeleteFixture(root);
        }
    }

    private static string CreateFixture(bool includeTargetBody)
    {
        var baseDirectory = Path.Combine(AppContext.BaseDirectory, "deletion-fixtures");
        var root = Path.Combine(baseDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sessions"));
        Directory.CreateDirectory(Path.Combine(root, "archived_sessions"));
        Directory.CreateDirectory(Path.Combine(root, "sqlite"));
        if (includeTargetBody)
        {
            File.WriteAllText(
                Path.Combine(root, "sessions", $"rollout-{TargetId}.jsonl"),
                JsonSerializer.Serialize(new
                {
                    type = "session_meta",
                    payload = new { id = TargetId, timestamp = "2026-08-15T00:00:00Z" }
                }));
        }

        using (var state = new SqliteConnection($"Data Source={Path.Combine(root, "state_5.sqlite")};Pooling=False"))
        {
            state.Open();
            using var command = state.CreateCommand();
            command.CommandText = "CREATE TABLE threads(id TEXT PRIMARY KEY); INSERT INTO threads VALUES ($target), ($other);";
            command.Parameters.AddWithValue("$target", TargetId);
            command.Parameters.AddWithValue("$other", OtherId);
            command.ExecuteNonQuery();
        }

        using (var catalog = new SqliteConnection($"Data Source={Path.Combine(root, "sqlite", "codex-dev.db")};Pooling=False"))
        {
            catalog.Open();
            using var command = catalog.CreateCommand();
            command.CommandText = """
                CREATE TABLE local_thread_catalog(host_id TEXT, thread_id TEXT, PRIMARY KEY(host_id, thread_id));
                CREATE TABLE local_thread_catalog_metadata(id INTEGER PRIMARY KEY, catalog_revision INTEGER NOT NULL);
                INSERT INTO local_thread_catalog VALUES ('local', $target), ('local', $other);
                INSERT INTO local_thread_catalog_metadata VALUES (1, 7);
                """;
            command.Parameters.AddWithValue("$target", TargetId);
            command.Parameters.AddWithValue("$other", OtherId);
            command.ExecuteNonQuery();
        }

        File.WriteAllText(Path.Combine(root, ".codex-global-state.json"), $$"""
            {
              "threadTitles": {
                "{{TargetId}}": "target",
                "{{OtherId}}": "other"
              },
              "recent": ["{{TargetId}}", "{{OtherId}}", "prefix-{{TargetId}}-suffix"]
            }
            """);
        File.WriteAllLines(Path.Combine(root, "session_index.jsonl"), [
            $$"""{"id":"{{TargetId}}","thread_name":"target"}""",
            $$"""{"id":"{{OtherId}}","thread_name":"other"}"""
        ]);
        return root;
    }

    private static async Task<IReadOnlyList<string>> ReadIdsAsync(string database, string table, string column)
    {
        var values = new List<string>();
        await using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} ORDER BY {column}";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<long> ReadScalarAsync(string database, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void DeleteFixture(string root)
    {
        var allowedRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "deletion-fixtures"));
        var target = Path.GetFullPath(root);
        Assert.StartsWith(allowedRoot + Path.DirectorySeparatorChar, target, StringComparison.OrdinalIgnoreCase);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }
}
