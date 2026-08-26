using CodexConversationManager.Core.LocalData;

namespace CodexConversationManager.Core.Import;

public sealed record ImportBackup(string Root, IReadOnlyList<(string Original, string Copy)> Files);

public static class ImportBackupService
{
    public static Task<ImportBackup> CreateAsync(CodexPaths paths, IEnumerable<string> destinationFiles,
        string backupRoot, CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(Path.GetFullPath(backupRoot), DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(root);
        var originals = new[] { paths.StateDatabase, paths.CatalogDatabase, paths.GlobalState }
            .Concat(destinationFiles).Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToList();
        var files = new List<(string Original, string Copy)>();
        foreach (var original in originals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var copy = Path.Combine(root, Path.GetFileName(original) + ".bak");
            File.Copy(original, copy, overwrite: true);
            files.Add((original, copy));
        }
        return Task.FromResult(new ImportBackup(root, files));
    }

    public static Task RestoreAsync(ImportBackup backup, IEnumerable<string> createdFiles,
        CancellationToken cancellationToken = default)
    {
        foreach (var file in createdFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!backup.Files.Any(item => string.Equals(item.Original, file, StringComparison.OrdinalIgnoreCase)) && File.Exists(file))
                File.Delete(file);
        }
        foreach (var (original, copy) in backup.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(copy, original, overwrite: true);
        }
        return Task.CompletedTask;
    }
}
