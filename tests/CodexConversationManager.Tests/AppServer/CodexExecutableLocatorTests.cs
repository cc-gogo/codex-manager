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

            var result = CodexExecutableLocator.Locate([packagedDirectory, npmDirectory]);

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
}
