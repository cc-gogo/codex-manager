using CodexConversationManager.Core.Backup;
using CodexConversationManager.Core.Domain;
using Xunit;

namespace CodexConversationManager.Tests.Backup;

public sealed class ConversationBackupServiceTests
{
    [Fact]
    public async Task Backup_current_only_overwrites_mirror_without_creating_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-backup-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source.jsonl");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(source, "automatic snapshot");
        try
        {
            var record = Record("33333333-3333-7333-8333-333333333333", "Auto", source);
            var result = await new ConversationBackupService().BackupAsync(
                [record], root, new DateTimeOffset(2026, 8, 16, 12, 34, 56, TimeSpan.FromHours(8)),
                ConversationBackupMode.CurrentOnly);

            Assert.True(File.Exists(Path.Combine(root, "current", "manifest.json")));
            Assert.False(Directory.Exists(Path.Combine(root, "history")));
            Assert.Equal(string.Empty, result.HistoryPath);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Backup_writes_current_mirror_history_snapshot_and_manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-backup-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source.jsonl");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(source, "original conversation");
        try
        {
            var record = Record("11111111-1111-7111-8111-111111111111", "Codex title", source);
            var result = await new ConversationBackupService().BackupAsync([record], root, new DateTimeOffset(2026, 8, 16, 12, 34, 56, TimeSpan.FromHours(8)));

            Assert.Equal("original conversation", await File.ReadAllTextAsync(Path.Combine(root, "current", "conversations", record.Id + ".jsonl")));
            Assert.Equal("original conversation", await File.ReadAllTextAsync(Path.Combine(root, "history", "20260816-123456", "conversations", record.Id + ".jsonl")));
            Assert.Contains("Codex title", await File.ReadAllTextAsync(Path.Combine(root, "current", "manifest.json")));
            Assert.Equal(1, result.CopiedFileCount);
            Assert.Equal(0, result.MissingFileCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Backup_overwrites_current_but_keeps_previous_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-backup-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source.jsonl");
        Directory.CreateDirectory(root);
        try
        {
            var record = Record("22222222-2222-7222-8222-222222222222", "Title", source);
            await File.WriteAllTextAsync(source, "first");
            await new ConversationBackupService().BackupAsync([record], root, new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.FromHours(8)));
            await File.WriteAllTextAsync(source, "second");
            await new ConversationBackupService().BackupAsync([record], root, new DateTimeOffset(2026, 8, 16, 13, 0, 0, TimeSpan.FromHours(8)));

            Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(root, "current", "conversations", record.Id + ".jsonl")));
            Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(root, "history", "20260816-120000", "conversations", record.Id + ".jsonl")));
            Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(root, "history", "20260816-130000", "conversations", record.Id + ".jsonl")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static ConversationRecord Record(string id, string title, string path) => new(
        id, title, ConversationCategory.Normal, "cli", "D:\\work", null, null, true,
        new ConversationEvidence { Id = id, ActiveSessionPaths = [path] });
}
