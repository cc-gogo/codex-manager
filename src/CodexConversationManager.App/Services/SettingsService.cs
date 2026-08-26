using System.Text.Json;
using System.IO;

namespace CodexConversationManager.App.Services;

public sealed record AppSettings(
    string? CodexHome = null,
    string? AutoBackupRoot = null,
    bool AutoBackupEnabled = false,
    int AutoBackupIntervalMinutes = 30,
    string Language = "Chinese");

public sealed class SettingsService(PortablePathService paths)
{
    public async Task<AppSettings> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.SettingsPath))
        {
            return new AppSettings();
        }

        await using var stream = new FileStream(
            paths.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false) ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        paths.EnsureDirectories();
        var temporaryPath = $"{paths.SettingsPath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, paths.SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
