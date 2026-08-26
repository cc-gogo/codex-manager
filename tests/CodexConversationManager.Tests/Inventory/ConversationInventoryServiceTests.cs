using System.Text.Json.Nodes;
using System.Diagnostics;
using CodexConversationManager.Core.AppServer;
using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.Inventory;
using CodexConversationManager.Core.LocalData;
using Xunit;

namespace CodexConversationManager.Tests.Inventory;

public sealed class ConversationInventoryServiceTests
{
    private const string RegressionIdOne = "019fd5b1-a888-7801-ab5b-6f1bbba8663f";
    private const string RegressionIdTwo = "019fd5c9-a9aa-7862-adf1-30a3319239cb";

    [Fact]
    public async Task Refresh_unions_all_sources_once_and_keeps_regression_ids_searchable()
    {
        var appServer = new FakeAppServerSource(
            active: [Thread("aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa")],
            archived: [Thread("bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb")]);
        var sessions = new FakeSessionSource(
        [
            Session(RegressionIdOne, "one.jsonl"),
            Session(RegressionIdTwo, "two.jsonl"),
            Session("aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa", "app.jsonl")
        ]);
        var states = new FakeStateSource(
        [
            State(RegressionIdOne, "First missing in Plus Plus", 100),
            State(RegressionIdTwo, "Second missing in Plus Plus", 200)
        ]);
        var catalog = new FakeCatalogSource(
        [
            Catalog(RegressionIdOne),
            Catalog(RegressionIdTwo),
            Catalog("cccccccc-cccc-7ccc-8ccc-cccccccccccc", missing: true)
        ]);
        var globals = new FakeGlobalSource(
        [
            new GlobalStateReference("cccccccc-cccc-7ccc-8ccc-cccccccccccc", "$.recent[0]")
        ]);
        var service = new ConversationInventoryService(
            appServer, sessions, states, catalog, globals, new ConversationClassifier());

        var snapshot = await service.RefreshAsync(InventoryMode.LiveCodex);

        Assert.Equal(snapshot.Records.Count, snapshot.Records.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(5, snapshot.Records.Count);
        Assert.Equal(ConversationCategory.Residual, snapshot.Records.Single(x => x.Id == RegressionIdOne).Category);
        Assert.Equal(ConversationCategory.Residual, snapshot.Records.Single(x => x.Id == RegressionIdTwo).Category);
        Assert.Contains(snapshot.Records, x => x.Id.Contains(RegressionIdOne, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.Records, x => x.Id.Contains(RegressionIdTwo, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ConversationCategory.Damaged, snapshot.Records.Single(x => x.Id.StartsWith("cccc", StringComparison.Ordinal)).Category);
        Assert.All(appServer.Calls, call => Assert.True(call.UseStateDbOnly));
        Assert.Empty(snapshot.SourceErrors);
        Assert.Contains(snapshot.Diagnostics, item => item.Source == "sessions" && item.RecordCount == 3 && item.Error is null);
    }

    [Fact]
    public async Task Failed_source_is_visible_while_other_sources_still_return_records()
    {
        var service = new ConversationInventoryService(
            new FakeAppServerSource([], []),
            new ThrowingSessionSource(),
            new FakeStateSource([]),
            new FakeCatalogSource([Catalog("dddddddd-dddd-7ddd-8ddd-dddddddddddd", missing: true)]),
            new FakeGlobalSource([]),
            new ConversationClassifier());

        var snapshot = await service.RefreshAsync(InventoryMode.LiveCodex);

        Assert.Single(snapshot.Records);
        Assert.Equal(ConversationCategory.Damaged, snapshot.Records[0].Category);
        Assert.Contains("sessions", snapshot.SourceErrors.Keys);
        Assert.Contains("fixture failure", snapshot.SourceErrors["sessions"], StringComparison.Ordinal);
        Assert.Contains(snapshot.Diagnostics, item => item.Source == "sessions" && item.Error?.Contains("fixture failure") == true);
    }

    [Fact]
    public async Task Global_state_only_project_ids_are_not_exposed_as_deletable_conversations()
    {
        const string projectId = "258efd5b-7210-4414-af05-5c82a33529a8";
        var service = new ConversationInventoryService(
            new FakeAppServerSource([], []),
            new FakeSessionSource([]), new FakeStateSource([]), new FakeCatalogSource([]),
            new FakeGlobalSource([new GlobalStateReference(projectId, "$.local-projects['258efd5b-7210-4414-af05-5c82a33529a8']")]),
            new ConversationClassifier());

        var snapshot = await service.RefreshLocalAsync(InventoryMode.LiveCodex);

        Assert.DoesNotContain(snapshot.Records, record => record.Id == projectId);
    }

    [Fact]
    public async Task RefreshLocal_returns_local_sessions_without_waiting_for_app_server()
    {
        var service = new ConversationInventoryService(
            new SlowAppServerSource(),
            new FakeSessionSource([Session("local-only", "local.jsonl")]),
            new FakeStateSource([]), new FakeCatalogSource([]), new FakeGlobalSource([]), new ConversationClassifier());
        var timer = Stopwatch.StartNew();

        var snapshot = await service.RefreshLocalAsync(InventoryMode.LiveCodex);

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Contains(snapshot.Records, row => row.Id == "local-only");
        Assert.Contains(snapshot.Diagnostics, item => item.Source == "app-server-active" && item.Status == InventoryReadStatus.Pending);
        Assert.DoesNotContain("app-server-active", snapshot.SourceErrors.Keys);
    }

    [Fact]
    public async Task State_recency_does_not_make_an_unlisted_session_a_normal_conversation()
    {
        const string id = "abababab-abab-7bab-8bab-abababababab";
        var service = new ConversationInventoryService(
            new FakeAppServerSource([], []),
            new FakeSessionSource([Session(id, "local.jsonl")]),
            new FakeStateSource([State(id, "Local only", 999)]),
            new FakeCatalogSource([]), new FakeGlobalSource([]), new ConversationClassifier());

        var record = Assert.Single((await service.RefreshLocalAsync(InventoryMode.LiveCodex)).Records);

        Assert.Equal(ConversationCategory.Residual, record.Category);
        Assert.False(record.Evidence.IsRecent);
    }

    [Fact]
    public async Task ReconcileAppServer_merges_app_server_results_into_the_local_snapshot()
    {
        var appServer = new DeferredAppServerSource();
        var service = new ConversationInventoryService(
            appServer,
            new FakeSessionSource([Session("local-only", "local.jsonl")]),
            new FakeStateSource([]), new FakeCatalogSource([]), new FakeGlobalSource([]), new ConversationClassifier());

        var local = await service.RefreshLocalAsync(InventoryMode.LiveCodex);
        var reconciliation = service.ReconcileAppServerAsync(local, InventoryMode.LiveCodex);
        appServer.Complete(false, [Thread("app-server-only")]);
        appServer.Complete(true, []);

        var snapshot = await reconciliation;

        Assert.Equal(["app-server-only", "local-only"], snapshot.Records.Select(row => row.Id).Order().ToArray());
        Assert.Contains(snapshot.Diagnostics, item => item.Source == "app-server-active" && item.Status == InventoryReadStatus.Completed && item.RecordCount == 1);
    }

    [Fact]
    public async Task Index_only_conversations_remain_damaged_after_app_server_reconciliation()
    {
        const string id = "99999999-9999-7999-8999-999999999999";
        const string indexPath = "C:\\Users\\ASUS\\.codex\\session_index.jsonl";
        var service = new ConversationInventoryService(
            new FakeAppServerSource([], []),
            new FakeSessionSource([]), new FakeStateSource([]), new FakeCatalogSource([]),
            new FakeGlobalSource([]), new ConversationClassifier(),
            new FakeSessionIndexSource([new SessionIndexEvidence(id, "Index only", DateTimeOffset.UtcNow, indexPath)]));

        var local = await service.RefreshLocalAsync(InventoryMode.LiveCodex);
        var reconciled = await service.ReconcileAppServerAsync(local, InventoryMode.LiveCodex);

        var record = Assert.Single(reconciled.Records);
        Assert.Equal(ConversationCategory.Damaged, record.Category);
        Assert.Equal(1, record.Evidence.SessionIndexRows);
        Assert.Equal([indexPath], record.Evidence.SessionIndexPaths);
    }

    [Fact]
    public async Task Refresh_prefers_the_current_codex_title_from_app_server()
    {
        const string id = "eeeeeeee-eeee-7eee-8eee-eeeeeeeeeeee";
        var appServer = new FakeAppServerSource([Thread(id, "Renamed in Codex")], []);
        var service = new ConversationInventoryService(
            appServer,
            new FakeSessionSource([]),
            new FakeStateSource([State(id, "Old local title", 100)]),
            new FakeCatalogSource([Catalog(id)]),
            new FakeGlobalSource([]),
            new ConversationClassifier());

        var snapshot = await service.RefreshAsync(InventoryMode.LiveCodex);

        Assert.Equal("Renamed in Codex", Assert.Single(snapshot.Records).DisplayTitle);
    }

    [Fact]
    public async Task Refresh_prefers_the_codex_sidebar_title_from_the_local_catalog()
    {
        const string id = "ffffffff-ffff-7fff-8fff-ffffffffffff";
        var service = new ConversationInventoryService(
            new FakeAppServerSource([], []),
            new FakeSessionSource([]),
            new FakeStateSource([State(id, "Old prompt summary", 100)]),
            new FakeCatalogSource([Catalog(id, "Codex sidebar title")]),
            new FakeGlobalSource([]),
            new ConversationClassifier());

        var snapshot = await service.RefreshAsync(InventoryMode.LiveCodex);

        Assert.Equal("Codex sidebar title", Assert.Single(snapshot.Records).DisplayTitle);
    }

    [Fact]
    public async Task Refresh_keeps_a_subagent_classification_when_another_source_labels_the_thread_interactive()
    {
        const string id = "11111111-1111-7111-8111-111111111111";
        var service = new ConversationInventoryService(
            new FakeAppServerSource([], []),
            new FakeSessionSource([
                new SessionEvidence(id, "thread.jsonl", false, "cli", "interactive", "D:\\work", DateTimeOffset.FromUnixTimeSeconds(100), null)
            ]),
            new FakeStateSource([
                new StateThreadEvidence(id, "thread.jsonl", "{\"subagent\":{}}", "subagent", "D:\\work", "Subagent", false,
                    DateTimeOffset.FromUnixTimeSeconds(100), DateTimeOffset.FromUnixTimeSeconds(200))
            ]),
            new FakeCatalogSource([]),
            new FakeGlobalSource([]),
            new ConversationClassifier());

        var record = Assert.Single((await service.RefreshLocalAsync(InventoryMode.LiveCodex)).Records);

        Assert.Equal(ConversationCategory.Normal, record.Category);
    }

    private static AppServerThread Thread(string id, string? name = null) => new(id, new JsonObject
    {
        ["id"] = id,
        ["name"] = name
    });

    private static SessionEvidence Session(string id, string path) => new(
        id, path, false, "vscode", "interactive", "D:\\work", DateTimeOffset.FromUnixTimeSeconds(100), null);

    private static StateThreadEvidence State(string id, string title, long updatedSeconds) => new(
        id, $"sessions/{id}.jsonl", "vscode", "interactive", "D:\\work", title, false,
        DateTimeOffset.FromUnixTimeSeconds(50), DateTimeOffset.FromUnixTimeSeconds(updatedSeconds));

    private static CatalogThreadEvidence Catalog(string id, string? title = null, bool missing = false) => new(
        id, "local", title ?? id, "vscode", "interactive", "D:\\work", missing,
        DateTimeOffset.FromUnixTimeSeconds(50), DateTimeOffset.FromUnixTimeSeconds(100));

    private sealed class FakeAppServerSource(
        IReadOnlyList<AppServerThread> active,
        IReadOnlyList<AppServerThread> archived) : IAppServerInventorySource
    {
        public List<(bool Archived, bool UseStateDbOnly)> Calls { get; } = [];

        public Task<ThreadListResult> ListAllThreadsAsync(
            bool isArchived,
            bool useStateDbOnly,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((isArchived, useStateDbOnly));
            return Task.FromResult(new ThreadListResult(isArchived ? archived : active, null));
        }
    }

    private sealed class SlowAppServerSource : IAppServerInventorySource
    {
        public async Task<ThreadListResult> ListAllThreadsAsync(bool archived, bool useStateDbOnly, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ThreadListResult([], null);
        }
    }

    private sealed class DeferredAppServerSource : IAppServerInventorySource
    {
        private readonly TaskCompletionSource<ThreadListResult> _active = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ThreadListResult> _archived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ThreadListResult> ListAllThreadsAsync(bool archived, bool useStateDbOnly, CancellationToken cancellationToken = default) =>
            archived ? _archived.Task.WaitAsync(cancellationToken) : _active.Task.WaitAsync(cancellationToken);

        public void Complete(bool archived, IReadOnlyList<AppServerThread> threads) =>
            (archived ? _archived : _active).TrySetResult(new ThreadListResult(threads, null));
    }

    private sealed class FakeSessionSource(IReadOnlyList<SessionEvidence> rows) : ISessionEvidenceSource
    {
        public Task<IReadOnlyList<SessionEvidence>> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(rows);
    }

    private sealed class ThrowingSessionSource : ISessionEvidenceSource
    {
        public Task<IReadOnlyList<SessionEvidence>> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<SessionEvidence>>(new IOException("fixture failure"));
    }

    private sealed class FakeStateSource(IReadOnlyList<StateThreadEvidence> rows) : IStateEvidenceSource
    {
        public Task<IReadOnlyList<StateThreadEvidence>> ReadThreadsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(rows);
    }

    private sealed class FakeCatalogSource(IReadOnlyList<CatalogThreadEvidence> rows) : ICatalogEvidenceSource
    {
        public Task<IReadOnlyList<CatalogThreadEvidence>> ReadCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(rows);
    }

    private sealed class FakeGlobalSource(IReadOnlyList<GlobalStateReference> rows) : IGlobalStateEvidenceSource
    {
        public Task<IReadOnlyList<GlobalStateReference>> ReadReferencesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(rows);
    }

    private sealed class FakeSessionIndexSource(IReadOnlyList<SessionIndexEvidence> rows) : ISessionIndexEvidenceSource
    {
        public Task<IReadOnlyList<SessionIndexEvidence>> ReadEntriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(rows);
    }
}
