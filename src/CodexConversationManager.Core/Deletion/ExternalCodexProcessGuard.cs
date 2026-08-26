using System.Diagnostics;

namespace CodexConversationManager.Core.Deletion;

public sealed record ProcessSnapshot(int ProcessId, string ProcessName);

public sealed record ProcessGuardResult(
    bool IsSafe,
    IReadOnlyList<ProcessSnapshot> BlockingProcesses);

public interface IProcessSnapshotSource
{
    Task<IReadOnlyList<ProcessSnapshot>> ReadAsync(CancellationToken cancellationToken = default);
}

public interface IDeletionProcessGuard
{
    Task<ProcessGuardResult> CheckAsync(
        IReadOnlySet<int> ownedPids,
        CancellationToken cancellationToken = default);
}

public sealed class SystemProcessSnapshotSource : IProcessSnapshotSource
{
    public Task<IReadOnlyList<ProcessSnapshot>> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshots = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    snapshots.Add(new ProcessSnapshot(process.Id, process.ProcessName));
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ProcessSnapshot>>(snapshots);
    }
}

public sealed class ExternalCodexProcessGuard(
    IProcessSnapshotSource snapshots,
    Func<TimeSpan, Task>? delay = null) : IDeletionProcessGuard
{
    private static readonly HashSet<string> BlockingNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ChatGPT",
        "codex",
        "codex-code-mode-host"
    };

    private readonly Func<TimeSpan, Task> _delay = delay ?? Task.Delay;

    public async Task<ProcessGuardResult> CheckAsync(
        IReadOnlySet<int> ownedPids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownedPids);
        var first = await snapshots.ReadAsync(cancellationToken).ConfigureAwait(false);
        await _delay(TimeSpan.FromMilliseconds(150)).WaitAsync(cancellationToken).ConfigureAwait(false);
        var second = await snapshots.ReadAsync(cancellationToken).ConfigureAwait(false);
        var blockers = first.Concat(second)
            .Where(process => BlockingNames.Contains(Path.GetFileNameWithoutExtension(process.ProcessName)))
            .Where(process => !ownedPids.Contains(process.ProcessId))
            .DistinctBy(process => process.ProcessId)
            .OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.ProcessId)
            .ToList();
        return new ProcessGuardResult(blockers.Count == 0, blockers);
    }
}
