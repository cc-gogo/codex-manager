using CodexConversationManager.Core.LocalData;

namespace CodexConversationManager.Core.Deletion;

public sealed class ThreadDeletionBackupService
{
    public async Task<ThreadDeletionBackup> CreateAsync(
        CodexPaths paths,
        IReadOnlyList<string> sessionPaths,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(paths.Root, $"recovery-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var files = new[]
            {
                paths.StateDatabase, paths.CatalogDatabase, paths.ThreadHistoryDatabase,
                paths.GlobalState, $"{paths.GlobalState}.bak", Path.Combine(paths.Root, "session_index.jsonl")
            }
            .Concat(sessionPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var entries = new List<ThreadDeletionBackup.Entry>();
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);
            var copy = Path.Combine(directory, $"{entries.Count:D4}.backup");
            var existed = File.Exists(fullPath);
            if (existed)
            {
                if (SqliteSnapshot.IsDatabase(fullPath)) SqliteSnapshot.Copy(fullPath, copy);
                else
                {
                    await using var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    await using var destination = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                }
            }

            entries.Add(new ThreadDeletionBackup.Entry(fullPath, copy, existed));
        }

        return new ThreadDeletionBackup(directory, entries);
    }
}

public sealed class ThreadDeletionBackup(string directory, IReadOnlyList<ThreadDeletionBackup.Entry> entries) : IAsyncDisposable
{
    private bool _keepForRecovery;
    public string DirectoryPath => directory;
    public sealed record Entry(string OriginalPath, string BackupPath, bool Existed);

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.Existed)
                {
                    if (File.Exists(entry.OriginalPath)) File.Delete(entry.OriginalPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath)!);
                if (SqliteSnapshot.IsDatabase(entry.OriginalPath)) SqliteSnapshot.Restore(entry.BackupPath, entry.OriginalPath);
                else
                {
                    await using var source = new FileStream(entry.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await using var destination = new FileStream(entry.OriginalPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            _keepForRecovery = true;
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_keepForRecovery && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        return ValueTask.CompletedTask;
    }
}
