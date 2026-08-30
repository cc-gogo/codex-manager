using CodexConversationManager.Core.LocalData;
using CodexConversationManager.Core.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodexConversationManager.Tests.Sync;

public sealed class ProviderSyncServiceTests
{
    [Fact]
    public async Task PreviewAndApply_UpdateEveryProviderMismatchAndBecomeIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-provider-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sessions", "2026"));
        Directory.CreateDirectory(Path.Combine(root, "archived_sessions"));
        await File.WriteAllTextAsync(Path.Combine(root, "config.toml"), "model_provider = \"CodexPlusPlus\"\n");
        await File.WriteAllLinesAsync(Path.Combine(root, "sessions", "2026", "rollout.jsonl"),
        ["{\"type\":\"session_meta\",\"payload\":{\"model_provider\":\"openai\"}}", "{\"type\":\"session_meta\",\"payload\":{\"model_provider\":\"custom\"}}"]);
        await CreateDbAsync(Path.Combine(root, "state_5.sqlite"), "threads");
        var paths = CodexPaths.FromRoot(root);
        var service = new ProviderSyncService(paths, Path.Combine(root, "config.toml"), Path.Combine(root, "backup"));

        var preview = await service.PreviewAsync();
        Assert.Equal(4, preview.TotalCount);
        var result = await service.ApplyAsync(preview);
        Assert.Equal(4, result.UpdatedCount);
        Assert.True(Directory.Exists(result.BackupPath));
        var second = await service.PreviewAsync();
        Assert.Equal(0, second.TotalCount);
        var rollout = await File.ReadAllTextAsync(Path.Combine(root, "sessions", "2026", "rollout.jsonl"));
        Assert.DoesNotContain("custom", rollout);
        Assert.Equal(2, rollout.Split("CodexPlusPlus", StringSplitOptions.None).Length - 1);
        Assert.Equal(0, await ReadCountAsync(Path.Combine(root, "state_5.sqlite"), "openai"));
        Assert.Equal(2, await ReadCountAsync(Path.Combine(root, "state_5.sqlite"), "CodexPlusPlus"));
        Directory.Delete(root, true);
    }

    private static async Task CreateDbAsync(string path, string table)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE {table} (id TEXT, model_provider TEXT); INSERT INTO {table} VALUES ('1','openai'); INSERT INTO {table} VALUES ('2','api-other');";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadCountAsync(string path, string provider)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*) FROM threads WHERE model_provider=$p"; command.Parameters.AddWithValue("$p", provider);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
