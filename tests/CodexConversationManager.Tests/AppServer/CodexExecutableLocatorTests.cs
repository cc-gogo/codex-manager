using CodexConversationManager.Core.AppServer;
using Xunit;

namespace CodexConversationManager.Tests.AppServer;

public sealed class CodexExecutableLocatorTests
{
    [Fact]
    public void Npm_native_binary_is_preferred_over_packaged_app_resource()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "locator-fixture", Guid.NewGuid().ToString("N"));
        var packagedDirectory = Path.Combine(root, "WindowsApps", "resources");
        var npmDirectory = Path.Combine(root, "npm");
        var nativeBinary = Path.Combine(
            npmDirectory,
            "node_modules", "@openai", "codex", "node_modules", "@openai",
            "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe");
        try
        {
            Directory.CreateDirectory(packagedDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(nativeBinary)!);
            File.WriteAllBytes(Path.Combine(packagedDirectory, "codex.exe"), [0]);
            File.WriteAllText(Path.Combine(npmDirectory, "codex.cmd"), "fixture");
            File.WriteAllBytes(nativeBinary, [0]);

            var result = CodexExecutableLocator.Locate(
                [packagedDirectory, npmDirectory], Path.Combine(root, "empty-local-app-data"),
                versionProbe: _ => "codex-cli 0.130.0");

            Assert.Equal(nativeBinary, result);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Locate_prefers_newer_desktop_binary_over_older_path_cli()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "locator-fixture", Guid.NewGuid().ToString("N"));
        var pathBin = Path.Combine(root, "path-bin");
        var localAppData = Path.Combine(root, "local-app-data");
        var desktopBinary = Path.Combine(
            localAppData, "OpenAI", "Codex", "bin", "desktop-0151", "codex.exe");
        var pathBinary = Path.Combine(pathBin, "codex.exe");

        try
        {
            Directory.CreateDirectory(pathBin);
            Directory.CreateDirectory(Path.GetDirectoryName(desktopBinary)!);
            File.WriteAllBytes(pathBinary, [0]);
            File.WriteAllBytes(desktopBinary, [0]);

            var result = CodexExecutableLocator.Locate(
                [pathBin], localAppData,
                path => path.Contains("desktop-0151", StringComparison.OrdinalIgnoreCase)
                    ? "codex-cli 0.151.0-alpha.7.2"
                    : "codex-cli 0.130.0");

            Assert.Equal(desktopBinary, result);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Locate_skips_candidates_that_cannot_report_a_version()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "locator-fixture", Guid.NewGuid().ToString("N"));
        var pathBin = Path.Combine(root, "path-bin");
        var localAppData = Path.Combine(root, "local-app-data");
        var brokenBinary = Path.Combine(pathBin, "codex.exe");
        var workingBinary = Path.Combine(
            localAppData, "OpenAI", "Codex", "bin", "working", "codex.exe");

        try
        {
            Directory.CreateDirectory(pathBin);
            Directory.CreateDirectory(Path.GetDirectoryName(workingBinary)!);
            File.WriteAllBytes(brokenBinary, [0]);
            File.WriteAllBytes(workingBinary, [0]);

            var result = CodexExecutableLocator.Locate(
                [pathBin], localAppData,
                path => path.Contains("working", StringComparison.OrdinalIgnoreCase)
                    ? "codex-cli 0.150.0-alpha.8"
                    : null);

            Assert.Equal(workingBinary, result);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
