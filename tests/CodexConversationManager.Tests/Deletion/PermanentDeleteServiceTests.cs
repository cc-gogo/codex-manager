using CodexConversationManager.Core.Deletion;
using Xunit;

namespace CodexConversationManager.Tests.Deletion;

public sealed class PermanentDeleteServiceTests
{
    [Fact]
    public async Task Local_delete_removes_each_selected_id_without_using_an_app_server()
    {
        var cleaner = new FakeLocalCleaner();
        var service = new LocalPermanentDeleteService(cleaner, new Dictionary<string, IReadOnlyList<string>>
        {
            ["normal"] = ["D:\\codex\\sessions\\normal.jsonl"],
            ["ghost"] = []
        });

        var results = await service.ExecuteAsync(new DeletionPlan(["normal"], ["ghost"], []));

        Assert.Equal(["ghost", "normal"], results.Select(result => result.Id).Order().ToArray());
        Assert.Equal(["normal", "ghost"], cleaner.Calls.Select(call => call.Id).ToArray());
        Assert.Equal("D:\\codex\\sessions\\normal.jsonl", Assert.Single(cleaner.Calls[0].Paths));
    }

    [Fact]
    public async Task Local_delete_reports_a_rewrite_risk_when_codex_is_running()
    {
        var service = new LocalPermanentDeleteService(
            new FakeLocalCleaner(), new Dictionary<string, IReadOnlyList<string>>(), codexMayRewriteIndexes: true);

        var result = Assert.Single(await service.ExecuteAsync(new DeletionPlan(["thread"], [], [])));

        Assert.Equal(DeletionStatus.DeletedWithRewriteRisk, result.Status);
        Assert.Contains("可能回写", result.Error);
    }
    [Fact]
    public async Task Official_failure_never_runs_local_cleanup_and_independent_targets_continue()
    {
        var server = new FakeServer(failIds: ["bad"]);
        var cleaner = new FakeCleaner(() => server.Disposed);
        var service = new PermanentDeleteService(
            new SafeGuard(), server, cleaner, new FakeAuditor(), new HashSet<int>());
        var plan = new DeletionPlan(["bad", "good"], [], []);

        var results = await service.ExecuteAsync(plan);

        Assert.Equal(DeletionStatus.OfficialDeleteFailed, results.Single(x => x.Id == "bad").Status);
        Assert.Equal(DeletionStatus.Deleted, results.Single(x => x.Id == "good").Status);
        Assert.DoesNotContain("bad", cleaner.Calls);
        Assert.True(server.Disposed);
    }

    [Fact]
    public async Task Server_is_disposed_before_ghost_cleanup_and_ancestor_status_is_preserved()
    {
        var server = new FakeServer([]);
        var cleaner = new FakeCleaner(() => server.Disposed);
        var service = new PermanentDeleteService(
            new SafeGuard(), server, cleaner, new FakeAuditor(), new HashSet<int>());
        var plan = new DeletionPlan(["parent"], ["ghost"], ["child"]);

        var results = await service.ExecuteAsync(plan);

        Assert.Equal(DeletionStatus.Deleted, results.Single(x => x.Id == "parent").Status);
        Assert.Equal(DeletionStatus.Deleted, results.Single(x => x.Id == "ghost").Status);
        Assert.Equal(DeletionStatus.DeletedByAncestor, results.Single(x => x.Id == "child").Status);
        Assert.Equal(["ghost"], cleaner.Calls);
    }

    [Fact]
    public async Task Successful_official_delete_returns_immediately_without_a_full_local_residual_scan()
    {
        var server = new FakeServer([]);
        var auditor = new FakeAuditor(residualIds: ["thread"]);
        var service = new PermanentDeleteService(
            new SafeGuard(), server, new FakeCleaner(() => true),
            auditor, new HashSet<int>());

        var results = await service.ExecuteAsync(new DeletionPlan(["thread"], [], []));

        Assert.Equal(DeletionStatus.Deleted, Assert.Single(results).Status);
        Assert.Equal(0, auditor.CallCount);
    }

    [Fact]
    public async Task External_Codex_process_allows_direct_delete_and_still_audits_residuals()
    {
        var server = new FakeServer([]);
        var cleaner = new FakeCleaner(() => true);
        var service = new PermanentDeleteService(
            new BlockingGuard(), server, cleaner, new FakeAuditor(), new HashSet<int>());

        var results = await service.ExecuteAsync(new DeletionPlan(["synthetic"], ["ghost"], []));

        Assert.Equal(DeletionStatus.Deleted, results.Single(result => result.Id == "synthetic").Status);
        Assert.Equal(DeletionStatus.Deleted, results.Single(result => result.Id == "ghost").Status);
        Assert.Equal(["ghost"], cleaner.Calls);
        Assert.True(server.Disposed);
    }

    private sealed class SafeGuard : IDeletionProcessGuard
    {
        public Task<ProcessGuardResult> CheckAsync(
            IReadOnlySet<int> ownedPids,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessGuardResult(true, []));
    }

    private sealed class BlockingGuard : IDeletionProcessGuard
    {
        public Task<ProcessGuardResult> CheckAsync(
            IReadOnlySet<int> ownedPids,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessGuardResult(false, [new ProcessSnapshot(42, "codex")]));
    }

    private sealed class FakeServer(IEnumerable<string> failIds) : IDeletionAppServer
    {
        private readonly HashSet<string> _failIds = new(failIds, StringComparer.Ordinal);
        public bool Disposed { get; private set; }

        public Task DeleteThreadAsync(string id, CancellationToken cancellationToken = default) =>
            _failIds.Contains(id)
                ? Task.FromException(new InvalidOperationException("official failure"))
                : Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeCleaner(Func<bool> isServerDisposed) : IGhostResidualCleaner
    {
        public List<string> Calls { get; } = [];

        public Task CleanupAsync(string id, CancellationToken cancellationToken = default)
        {
            Assert.True(isServerDisposed());
            Calls.Add(id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditor(IEnumerable<string>? residualIds = null) : IResidualAuditor
    {
        private readonly HashSet<string> _residualIds = new(residualIds ?? [], StringComparer.Ordinal);
        public int CallCount { get; private set; }
        public Task<bool> HasResidualsAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasResidual(id));

        private bool HasResidual(string id)
        {
            CallCount++;
            return _residualIds.Contains(id);
        }
    }

    private sealed class FakeLocalCleaner : ILocalThreadCleaner
    {
        public List<(string Id, IReadOnlyList<string> Paths)> Calls { get; } = [];

        public Task DeleteLocalThreadAsync(string id, IReadOnlyList<string> knownSessionPaths, CancellationToken cancellationToken = default)
        {
            Calls.Add((id, knownSessionPaths));
            return Task.CompletedTask;
        }
    }
}
