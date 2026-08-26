using System.IO;

namespace CodexConversationManager.App.Services;

public sealed class PortablePathService(string? baseDirectory = null)
{
    public string Root { get; } = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private bool IsInstalled => File.Exists(UnderRoot("install-mode.txt"));
    private string InstalledDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexConversationManager");

    public string DataDirectory => IsInstalled ? Path.Combine(InstalledDataRoot, "data") : UnderRoot("data");
    public string LogsDirectory => IsInstalled ? Path.Combine(InstalledDataRoot, "logs") : UnderRoot("logs");
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    private string UnderRoot(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!fullPath.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Portable application data must stay beneath the application directory.");
        }

        return fullPath;
    }
}
