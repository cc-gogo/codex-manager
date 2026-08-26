using System.Text.Json;
using System.Text.Json.Serialization;
using CodexConversationManager.Core.Domain;

namespace CodexConversationManager.Core.Backup;

public sealed record ConversationBackupResult(
    string CurrentPath,
    string HistoryPath,
    int ConversationCount,
    int CopiedFileCount,
    int MissingFileCount);

public enum ConversationBackupMode
{
    CurrentAndHistory,
    CurrentOnly
}

public sealed class ConversationBackupService
{
    public async Task<ConversationBackupResult> BackupAsync(
        IReadOnlyList<ConversationRecord> records,
        string destinationRoot,
        DateTimeOffset? timestamp = null,
        ConversationBackupMode mode = ConversationBackupMode.CurrentAndHistory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        if (records.Count == 0) throw new ArgumentException("At least one conversation is required.", nameof(records));

        var root = Path.GetFullPath(destinationRoot);
        var current = Path.Combine(root, "current");
        var stamp = (timestamp ?? DateTimeOffset.Now).ToLocalTime().ToString("yyyyMMdd-HHmmss");
        var history = Path.Combine(root, "history", stamp);
        Directory.CreateDirectory(root);
        if (Directory.Exists(current)) Directory.Delete(current, recursive: true);
        Directory.CreateDirectory(current);
        if (mode == ConversationBackupMode.CurrentAndHistory) Directory.CreateDirectory(history);

        var currentResult = await WriteSnapshotAsync(records, current, cancellationToken).ConfigureAwait(false);
        if (mode == ConversationBackupMode.CurrentOnly)
        {
            return new ConversationBackupResult(current, string.Empty, records.Count,
                currentResult.CopiedFileCount, currentResult.MissingFileCount);
        }

        var historyResult = await WriteSnapshotAsync(records, history, cancellationToken).ConfigureAwait(false);
        return new ConversationBackupResult(current, history, records.Count,
            currentResult.CopiedFileCount, currentResult.MissingFileCount);
    }

    private static async Task<SnapshotResult> WriteSnapshotAsync(
        IReadOnlyList<ConversationRecord> records,
        string directory,
        CancellationToken cancellationToken)
    {
        var conversationsDirectory = Path.Combine(directory, "conversations");
        Directory.CreateDirectory(conversationsDirectory);
        var entries = new List<ConversationBackupEntry>();
        var copied = 0;
        var missing = 0;
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paths = record.Evidence.ActiveSessionPaths
                .Concat(record.Evidence.ArchivedSessionPaths)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var outputFiles = new List<string>();
            for (var index = 0; index < paths.Count; index++)
            {
                var fileName = paths.Count == 1 ? $"{record.Id}.jsonl" : $"{record.Id}-{index + 1}.jsonl";
                var target = Path.Combine(conversationsDirectory, fileName);
                await using var source = new FileStream(paths[index], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                await using var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                outputFiles.Add(Path.Combine("conversations", fileName));
                copied++;
            }

            if (outputFiles.Count == 0) missing++;
            entries.Add(new ConversationBackupEntry(record.Id, record.DisplayTitle, record.Cwd, outputFiles));
        }

        var manifest = new ConversationBackupManifest(DateTimeOffset.Now, entries);
        await using var manifestStream = File.Create(Path.Combine(directory, "manifest.json"));
        await JsonSerializer.SerializeAsync(manifestStream, manifest, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }, cancellationToken).ConfigureAwait(false);
        return new SnapshotResult(copied, missing);
    }

    private sealed record SnapshotResult(int CopiedFileCount, int MissingFileCount);
}

public sealed record ConversationBackupManifest(DateTimeOffset CreatedAt, IReadOnlyList<ConversationBackupEntry> Conversations);
public sealed record ConversationBackupEntry(string Id, string Title, string? Cwd, IReadOnlyList<string> Files);
