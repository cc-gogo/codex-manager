using CodexConversationManager.App.Services;
using Xunit;

namespace CodexConversationManager.Tests.Services;

public sealed class PortablePathServiceTests
{
    [Fact]
    public void Base_directory_with_trailing_separator_allows_settings_path()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "codex-manager-path-test") + Path.DirectorySeparatorChar;
        var paths = new PortablePathService(baseDirectory);

        Assert.StartsWith(Path.GetFullPath(baseDirectory), paths.SettingsPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installed_version_uses_local_application_data_for_writable_files()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"codex-manager-installed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(baseDirectory);
        File.WriteAllText(Path.Combine(baseDirectory, "install-mode.txt"), "installed");
        try
        {
            var paths = new PortablePathService(baseDirectory);

            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexConversationManager", "data", "settings.json");
            Assert.Equal(expected, paths.SettingsPath);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }
}
