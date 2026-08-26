using System.Diagnostics;

namespace CodexConversationManager.App.Services;

public sealed record ManagedProcess(int Id, string Name);
public sealed record CodexRestartResult(IReadOnlyList<string> Warnings);

public interface IManagedProcessRuntime
{
    IReadOnlyList<ManagedProcess> GetProcesses();
    Task StopAsync(int processId, CancellationToken cancellationToken = default);
    void LaunchDesktopApp(string appId);
}

public sealed class CodexDesktopRestartService(IManagedProcessRuntime? runtime = null)
{
    public const string DesktopAppId = "OpenAI.Codex_2p2nqsd0c76g0!App";
    private readonly IManagedProcessRuntime _runtime = runtime ?? new SystemManagedProcessRuntime();

    public async Task<CodexRestartResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        var result = await StopProcessesAsync(
            _runtime.GetProcesses().Where(process => string.Equals(process.Name, "ChatGPT", StringComparison.OrdinalIgnoreCase)),
            cancellationToken).ConfigureAwait(false);
        _runtime.LaunchDesktopApp(DesktopAppId);
        return result;
    }

    public Task<CodexRestartResult> StopCodexAsync(CancellationToken cancellationToken = default) =>
        StopProcessesAsync(_runtime.GetProcesses().Where(process =>
            process.Name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
            process.Name.Equals("codex", StringComparison.OrdinalIgnoreCase) ||
            process.Name.Equals("codex-code-mode-host", StringComparison.OrdinalIgnoreCase)), cancellationToken);

    public async Task<CodexRestartResult> RestartCodexAsync(CancellationToken cancellationToken = default)
    {
        var result = await StopCodexAsync(cancellationToken).ConfigureAwait(false);
        _runtime.LaunchDesktopApp(DesktopAppId);
        return result;
    }

    private async Task<CodexRestartResult> StopProcessesAsync(
        IEnumerable<ManagedProcess> processSequence,
        CancellationToken cancellationToken)
    {
        var targets = processSequence.ToList();
        var warnings = new List<string>();
        foreach (var process in targets)
        {
            try
            {
                await _runtime.StopAsync(process.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add($"未能关闭 {process.Name}（进程 {process.Id}）：{exception.Message}");
            }
        }

        return new CodexRestartResult(warnings);
    }
}

public sealed class SystemManagedProcessRuntime : IManagedProcessRuntime
{
    public IReadOnlyList<ManagedProcess> GetProcesses()
    {
        var processes = new List<ManagedProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { processes.Add(new ManagedProcess(process.Id, process.ProcessName)); }
                catch (InvalidOperationException) { }
            }
        }

        return processes;
    }

    public async Task StopAsync(int processId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return;
            }

            process.CloseMainWindow();
            var exited = await Task.WhenAny(process.WaitForExitAsync(cancellationToken),
                Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)).ConfigureAwait(false);
            if (exited != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ArgumentException)
        {
            // The process exited while the restart was starting.
        }
        catch (InvalidOperationException)
        {
            // The process exited while the restart was starting.
        }
    }

    public void LaunchDesktopApp(string appId)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}")
        {
            UseShellExecute = true
        });
    }
}
