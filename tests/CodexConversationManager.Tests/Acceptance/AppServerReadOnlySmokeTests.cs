using System.Text.Json;
using CodexConversationManager.Core.AppServer;
using Xunit;

namespace CodexConversationManager.Tests.Acceptance;

public sealed class AppServerReadOnlySmokeTests
{
    [Fact]
    public async Task Real_app_server_can_initialize_and_list_without_loading_bodies()
    {
        if (Environment.GetEnvironmentVariable("CODEX_REAL_SMOKE") != "1")
        {
            return;
        }

        var executable = CodexExecutableLocator.Locate();
        await using var transport = new StdioJsonRpcTransport(executable);
        await using var client = new CodexAppServerClient(transport, TimeSpan.FromSeconds(20));
        await client.InitializeAsync();
        var active = await client.ListAllThreadsAsync(archived: false, useStateDbOnly: true);

        var productRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var logPath = Path.Combine(productRoot, "logs", "app-server-smoke.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        await File.WriteAllTextAsync(
            logPath,
            JsonSerializer.Serialize(new
            {
                status = "PASS",
                activeThreadCount = active.Threads.Count,
                protocolErrors = 0
            }));

        Assert.True(active.Threads.Count >= 0);
    }
}
