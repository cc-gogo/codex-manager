using CodexConversationManager.App.Services;
using Xunit;

namespace CodexConversationManager.Tests.Services;

public sealed class CodexDesktopRestartServiceTests
{
    [Fact]
    public async Task Restart_stops_only_the_codex_desktop_processes_then_launches_the_desktop_app()
    {
        var runtime = new FakeRuntime([
            new ManagedProcess(1, "codex"),
            new ManagedProcess(2, "ChatGPT"),
            new ManagedProcess(3, "explorer")]);

        await new CodexDesktopRestartService(runtime).RestartAsync();

        Assert.Equal([2], runtime.StoppedIds);
        Assert.Equal(CodexDesktopRestartService.DesktopAppId, runtime.LaunchedAppId);
    }

    [Fact]
    public async Task Restart_still_launches_codex_when_one_existing_process_cannot_be_closed()
    {
        var runtime = new FakeRuntime([
            new ManagedProcess(1, "codex"),
            new ManagedProcess(2, "ChatGPT")], failIds: [2]);

        var result = await new CodexDesktopRestartService(runtime).RestartAsync();

        Assert.Equal(CodexDesktopRestartService.DesktopAppId, runtime.LaunchedAppId);
        Assert.Single(result.Warnings);
        Assert.Contains("2", result.Warnings[0]);
    }

    [Fact]
    public async Task StopCodex_stops_all_codex_processes_without_launching_the_desktop_app()
    {
        var runtime = new FakeRuntime([
            new ManagedProcess(1, "codex"),
            new ManagedProcess(2, "ChatGPT"),
            new ManagedProcess(3, "codex-code-mode-host"),
            new ManagedProcess(4, "explorer")]);

        await new CodexDesktopRestartService(runtime).StopCodexAsync();

        Assert.Equal([1, 2, 3], runtime.StoppedIds);
        Assert.Null(runtime.LaunchedAppId);
    }

    private sealed class FakeRuntime(IReadOnlyList<ManagedProcess> processes, IEnumerable<int>? failIds = null) : IManagedProcessRuntime
    {
        private readonly HashSet<int> _failIds = new(failIds ?? []);
        public List<int> StoppedIds { get; } = [];
        public string? LaunchedAppId { get; private set; }

        public IReadOnlyList<ManagedProcess> GetProcesses() => processes;

        public Task StopAsync(int processId, CancellationToken cancellationToken = default)
        {
            if (_failIds.Contains(processId))
            {
                return Task.FromException(new InvalidOperationException("access denied"));
            }

            StoppedIds.Add(processId);
            return Task.CompletedTask;
        }

        public void LaunchDesktopApp(string appId) => LaunchedAppId = appId;
    }
}
