using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Domain;
using Xunit;

namespace CodexConversationManager.Tests.Deletion;

public sealed class DeletionSafetyTests
{
    [Fact]
    public async Task Owned_app_server_is_ignored_but_external_codex_process_blocks()
    {
        var snapshots = new FakeProcessSnapshots(
        [
            [new ProcessSnapshot(10, "codex"), new ProcessSnapshot(20, "ChatGPT"), new ProcessSnapshot(30, "explorer")],
            [new ProcessSnapshot(10, "codex"), new ProcessSnapshot(20, "ChatGPT"), new ProcessSnapshot(30, "explorer")]
        ]);
        var guard = new ExternalCodexProcessGuard(snapshots, _ => Task.CompletedTask);

        var result = await guard.CheckAsync(new HashSet<int> { 10 });

        Assert.False(result.IsSafe);
        var blocker = Assert.Single(result.BlockingProcesses);
        Assert.Equal(20, blocker.ProcessId);
        Assert.Equal("ChatGPT", blocker.ProcessName);
    }

    [Fact]
    public async Task Process_appearing_only_in_second_sample_still_blocks()
    {
        var snapshots = new FakeProcessSnapshots(
        [
            [],
            [new ProcessSnapshot(44, "codex-code-mode-host")]
        ]);
        var guard = new ExternalCodexProcessGuard(snapshots, _ => Task.CompletedTask);

        var result = await guard.CheckAsync(new HashSet<int>());

        Assert.False(result.IsSafe);
        Assert.Contains(result.BlockingProcesses, process => process.ProcessId == 44);
        Assert.Equal(2, snapshots.ReadCount);
    }

    [Fact]
    public async Task Unrelated_processes_allow_deletion()
    {
        var snapshots = new FakeProcessSnapshots(
        [
            [new ProcessSnapshot(50, "powershell")],
            [new ProcessSnapshot(50, "powershell")]
        ]);
        var guard = new ExternalCodexProcessGuard(snapshots, _ => Task.CompletedTask);

        var result = await guard.CheckAsync(new HashSet<int>());

        Assert.True(result.IsSafe);
        Assert.Empty(result.BlockingProcesses);
    }

    [Fact]
    public void Parent_with_descendants_is_blocked_from_deletion_to_prevent_cascade()
    {
        var parent = Record("aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa", official: true,
            descendants: ["bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb"]);
        var child = Record("bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb", official: true);

        var plan = new DeletionPlanBuilder().Build([child, parent]);

        Assert.Equal([child.Id], plan.OfficialDeleteRootIds);
        Assert.Empty(plan.DeletedByAncestorIds);
        Assert.Equal([parent.Id], plan.BlockedByDescendantIds);
        Assert.Empty(plan.GhostCleanupIds);
    }

    [Fact]
    public void Official_and_ghost_targets_are_separated_in_stable_order()
    {
        var official = Record("dddddddd-dddd-7ddd-8ddd-dddddddddddd", official: true);
        var ghostB = Record("ffffffff-ffff-7fff-8fff-ffffffffffff", official: false);
        var ghostA = Record("eeeeeeee-eeee-7eee-8eee-eeeeeeeeeeee", official: false);

        var plan = new DeletionPlanBuilder().Build([ghostB, official, ghostA]);

        Assert.Equal([official.Id], plan.OfficialDeleteRootIds);
        Assert.Equal([ghostA.Id, ghostB.Id], plan.GhostCleanupIds);
    }

    [Fact]
    public void Local_delete_plan_includes_duplicate_and_corrupt_records_without_an_app_server_entry()
    {
        var duplicate = new ConversationRecord(
            "aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa", "duplicate", ConversationCategory.Duplicate,
            null, null, null, null, false, ConversationEvidence.Empty("aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa"));
        var corrupt = new ConversationRecord(
            "bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb", "corrupt", ConversationCategory.Damaged,
            null, null, null, null, false, ConversationEvidence.Empty("bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb"));

        var plan = new DeletionPlanBuilder().Build([duplicate, corrupt]);

        Assert.Equal([duplicate.Id, corrupt.Id], plan.OfficialDeleteRootIds);
        Assert.Empty(plan.GhostCleanupIds);
    }

    private static ConversationRecord Record(
        string id,
        bool official,
        IReadOnlyList<string>? descendants = null)
    {
        var evidence = ConversationEvidence.Empty(id) with
        {
            AppServerListed = official,
            CatalogRows = 1,
            DescendantIds = descendants ?? []
        };
        return new ConversationRecord(
            id, id, official ? ConversationCategory.Normal : ConversationCategory.Ghost,
            null, null, null, null, official, evidence);
    }

    private sealed class FakeProcessSnapshots(IEnumerable<IReadOnlyList<ProcessSnapshot>> snapshots)
        : IProcessSnapshotSource
    {
        private readonly Queue<IReadOnlyList<ProcessSnapshot>> _snapshots = new(snapshots);
        public int ReadCount { get; private set; }

        public Task<IReadOnlyList<ProcessSnapshot>> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(_snapshots.Dequeue());
        }
    }
}
