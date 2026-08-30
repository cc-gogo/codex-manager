using CodexConversationManager.App.ViewModels;
using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.Inventory;
using CodexConversationManager.Core.LocalData;
using Xunit;

namespace CodexConversationManager.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task Refresh_defaults_to_normal_conversations()
    {
        var normal = Record("normal", "Normal", ConversationCategory.Normal);
        var archived = Record("archived", "Archived", ConversationCategory.Archived);
        var viewModel = new MainViewModel(new FakeInventory([normal, archived]), new SafeGuard());

        await viewModel.RefreshAsync();

        Assert.Equal(ConversationCategory.Normal, viewModel.SelectedCategory);
        Assert.Equal([normal.Id], viewModel.VisibleRows.Select(row => row.Id).ToArray());
    }

    [Fact]
    public async Task Refresh_keeps_all_records_and_filters_by_category_or_full_id()
    {
        var first = Record("019fd5b1-a888-7801-ab5b-6f1bbba8663f", "First", ConversationCategory.Normal);
        var second = Record("019fd5c9-a9aa-7862-adf1-30a3319239cb", "Second", ConversationCategory.Ghost);
        var viewModel = new MainViewModel(
            new FakeInventory([first, second]), new SafeGuard());

        await viewModel.RefreshAsync();
        viewModel.SelectedCategory = ConversationCategory.Ghost;

        Assert.Single(viewModel.VisibleRows);
        Assert.Equal(second.Id, viewModel.VisibleRows[0].Id);
        viewModel.SelectedCategory = null;
        viewModel.SearchText = first.Id;
        Assert.Single(viewModel.VisibleRows);
        Assert.Equal(first.Id, viewModel.VisibleRows[0].Id);
    }

    [Fact]
    public async Task External_Codex_process_allows_direct_delete_but_warns_about_possible_residuals()
    {
        var record = Record("019fd5b1-a888-7801-ab5b-6f1bbba8663f", "First", ConversationCategory.Normal);
        var viewModel = new MainViewModel(
            new FakeInventory([record]), new BlockingGuard());

        await viewModel.RefreshAsync();

        Assert.Single(viewModel.VisibleRows);
        Assert.True(viewModel.CanDelete);
        Assert.Contains("运行", viewModel.DeletionStatus);
        Assert.Contains("重启 Codex", viewModel.DeletionStatus);
    }

    [Fact]
    public async Task Project_tree_assigns_a_shared_root_to_only_the_first_codex_project()
    {
        var sharedRoot = "D:\\codex\\日常对话";
        var record = Record("thread", "Shared", ConversationCategory.Normal, sharedRoot);
        var sidebar = new FakeSidebar(new CodexProjectSidebarSnapshot(
        [
            new CodexProject("daily", "日常对话", [sharedRoot], 1),
            new CodexProject("nextsay", "nextsay", ["D:\\codex\\nextsay", sharedRoot], 2)
        ],
        new Dictionary<string, string>(),
        new Dictionary<string, IReadOnlyList<string>>(),
        [],
        [record.Id]));
        var viewModel = new MainViewModel(new FakeInventory([record]), new SafeGuard(), projectSidebar: sidebar);

        await viewModel.RefreshAsync();

        var daily = Assert.Single(viewModel.ProjectTree, node => node.Name == "日常对话");
        Assert.Contains(daily.Children, node => node.Conversation?.Id == record.Id);
        Assert.DoesNotContain(viewModel.ProjectTree, node => node.Name == "nextsay");
        Assert.DoesNotContain(viewModel.ProjectTree, node => node.IsRecent);
    }

    [Fact]
    public async Task Project_conversation_does_not_also_appear_in_recent_conversations()
    {
        var record = Record("project-thread", "Project conversation", ConversationCategory.Normal, "D:\\codex\\日常对话");
        var sidebar = new FakeSidebar(new CodexProjectSidebarSnapshot(
        [
            new CodexProject("daily", "日常对话", ["D:\\codex\\日常对话"], 1)
        ],
        new Dictionary<string, string>(),
        new Dictionary<string, IReadOnlyList<string>>()));
        var viewModel = new MainViewModel(new FakeInventory([record]), new SafeGuard(), projectSidebar: sidebar);

        await viewModel.RefreshAsync();

        Assert.Contains(viewModel.ProjectTree, node => node.Name == "日常对话");
        Assert.DoesNotContain(viewModel.ProjectTree, node => node.IsRecent);
    }

    [Fact]
    public async Task Owned_app_server_pid_is_passed_to_the_deletion_guard()
    {
        var guard = new RecordingGuard();
        var viewModel = new MainViewModel(new FakeInventory([]), guard, new HashSet<int> { 25 });

        await viewModel.RefreshAsync();

        Assert.Contains(25, guard.OwnedPids);
        Assert.True(viewModel.CanDelete);
    }

    [Fact]
    public async Task Selecting_a_row_loads_its_structured_detail_on_demand()
    {
        var record = Record("019fd5b1-a888-7801-ab5b-6f1bbba8663f", "First", ConversationCategory.Normal);
        var viewModel = new MainViewModel(
            new FakeInventory([record]), new SafeGuard(), detailProvider: new FakeDetails());
        await viewModel.RefreshAsync();

        await viewModel.SelectAsync(Assert.Single(viewModel.Rows));

        Assert.Equal(record.Id, viewModel.SelectedRow!.Id);
        var block = Assert.Single(viewModel.DetailBlocks);
        Assert.Equal("Hello detail", block.Text);
    }

    [Fact]
    public async Task Loading_detail_for_export_does_not_require_the_row_to_be_open_in_the_detail_pane()
    {
        var record = Record("checked", "Checked only", ConversationCategory.Normal);
        var viewModel = new MainViewModel(
            new FakeInventory([record]), new SafeGuard(), detailProvider: new FakeDetails());
        await viewModel.RefreshAsync();
        var row = Assert.Single(viewModel.Rows);
        row.IsSelected = true;

        var detail = await viewModel.LoadDetailAsync(row);

        Assert.Equal(record.Id, detail.Id);
        Assert.Equal("Hello detail", Assert.Single(detail.Blocks).Text);
        Assert.Null(viewModel.SelectedRow);
    }

    [Fact]
    public async Task Category_filtered_project_tree_keeps_recent_node_for_projectless_state_records()
    {
        var archived = Record("archived", "Archived", ConversationCategory.Archived, "D:\\work\\one");
        var unassigned = Record("unassigned", "No cwd", ConversationCategory.Archived, null);
        var sidebar = new FakeSidebar(new CodexProjectSidebarSnapshot(
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            [],
            [])
        {
            ArchivedRecentThreadIds = [archived.Id]
        });
        var viewModel = new MainViewModel(new FakeInventory([archived, unassigned]), new SafeGuard(), projectSidebar: sidebar);

        await viewModel.RefreshAsync();
        viewModel.SelectedCategory = ConversationCategory.Archived;

        Assert.Contains(viewModel.ProjectTree, node => node.IsRecent);
    }

    [Fact]
    public async Task Archived_project_conversation_stays_under_its_project_even_when_it_is_archived_recent()
    {
        var archived = Record("archived-recent", "Archived recent", ConversationCategory.Archived, "D:\\AI\\railway");
        var sidebar = new FakeSidebar(new CodexProjectSidebarSnapshot(
        [
            new CodexProject("railway", "railway", ["D:\\AI\\railway"], 1)
        ],
        new Dictionary<string, string> { [archived.Id] = "railway" },
        new Dictionary<string, IReadOnlyList<string>>(),
        [],
        [])
        {
            ArchivedRecentThreadIds = [archived.Id]
        });
        var viewModel = new MainViewModel(new FakeInventory([archived]), new SafeGuard(), projectSidebar: sidebar);

        await viewModel.RefreshAsync();
        viewModel.SelectedCategory = ConversationCategory.Archived;

        var project = Assert.Single(viewModel.ProjectTree, node => node.Name == "railway");
        Assert.Contains(project.Children, node => node.Conversation?.Id == archived.Id);
        Assert.DoesNotContain(viewModel.ProjectTree, node => node.Name == "最近文件夹");
    }

    [Fact]
    public async Task Archived_project_conversation_is_excluded_from_the_normal_category()
    {
        var archived = Record("archived-project", "Archived project", ConversationCategory.Archived, "D:\\AI\\railway");
        var sidebar = new FakeSidebar(new CodexProjectSidebarSnapshot(
        [
            new CodexProject("railway", "railway", ["D:\\AI\\railway"], 1)
        ],
        new Dictionary<string, string> { [archived.Id] = "railway" },
        new Dictionary<string, IReadOnlyList<string>>(),
        [],
        [])
        {
            ArchivedRecentThreadIds = [archived.Id]
        });
        var viewModel = new MainViewModel(new FakeInventory([archived]), new SafeGuard(), projectSidebar: sidebar);

        await viewModel.RefreshAsync();

        Assert.Empty(viewModel.VisibleRows);
        Assert.Empty(viewModel.ProjectTree);
    }

    [Fact]
    public async Task Selecting_recent_node_limits_visible_rows_to_recent_records()
    {
        var first = Record("first", "One", ConversationCategory.Normal, "D:\\work\\one");
        var second = Record("second", "Two", ConversationCategory.Normal, "D:\\work\\two");
        var third = Record("third", "Three", ConversationCategory.Normal, "D:\\work\\three");
        var sidebar = new FakeSidebar(new CodexProjectSidebarSnapshot(
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            [],
            [second.Id, first.Id]));
        var viewModel = new MainViewModel(new FakeInventory([first, second, third]), new SafeGuard(), projectSidebar: sidebar);

        await viewModel.RefreshAsync();
        viewModel.SelectedProjectNode = Assert.Single(viewModel.ProjectTree, node => node.IsRecent);

        Assert.Equal([second.Id, first.Id], viewModel.SelectedProjectNode.Children
            .Select(node => node.Conversation!.Id).ToArray());
        Assert.Equal(2, viewModel.VisibleRows.Count);
        Assert.DoesNotContain(viewModel.VisibleRows, row => row.Id == third.Id);
    }

    [Fact]
    public async Task Normal_and_subagent_categories_both_expose_subagent_conversations()
    {
        var normal = Record("normal", "Normal", ConversationCategory.Normal);
        var subagent = Record("subagent", "Subagent", ConversationCategory.Normal, isSubAgent: true);
        var sidebar = new FakeSidebar(new CodexProjectSidebarSnapshot(
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            [],
            [normal.Id]));
        var viewModel = new MainViewModel(new FakeInventory([normal, subagent]), new SafeGuard(), projectSidebar: sidebar);

        await viewModel.RefreshAsync();

        var normalSubagents = Assert.Single(viewModel.ProjectTree, node => node.Name == "子代理对话");
        Assert.Equal([subagent.Id], normalSubagents.Children.Select(node => node.Conversation!.Id).ToArray());

        viewModel.SelectedCategory = ConversationCategory.SubAgent;

        var subagentCategory = Assert.Single(viewModel.ProjectTree, node => node.Name == "子代理对话");
        Assert.Equal([subagent.Id], subagentCategory.Children.Select(node => node.Conversation!.Id).ToArray());
        Assert.Equal([subagent.Id], viewModel.VisibleRows.Select(row => row.Id).ToArray());
    }

    [Fact]
    public async Task Normal_tree_exposes_unassigned_pinned_and_sectioned_sidebar_conversations()
    {
        var pinned = Record("pinned", "Pinned", ConversationCategory.Normal);
        var sectioned = Record("sectioned", "Sectioned", ConversationCategory.Normal);
        var sidebar = new FakeSidebar(new CodexProjectSidebarSnapshot(
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            [], [])
        {
            PinnedThreadIds = [pinned.Id],
            ThreadSectionIds = new Dictionary<string, string> { [sectioned.Id] = "research" },
            ThreadSections = [new CodexThreadSection("research", "Research")]
        });
        var viewModel = new MainViewModel(new FakeInventory([pinned, sectioned]), new SafeGuard(), projectSidebar: sidebar);

        await viewModel.RefreshAsync();

        var pinnedNode = Assert.Single(viewModel.ProjectTree, node => node.Name == "置顶对话");
        Assert.Equal([pinned.Id], pinnedNode.Children.Select(node => node.Conversation!.Id).ToArray());
        var sectionNode = Assert.Single(viewModel.ProjectTree, node => node.Name == "Research");
        Assert.Equal([sectioned.Id], sectionNode.Children.Select(node => node.Conversation!.Id).ToArray());
    }

    [Fact]
    public async Task Project_sidebar_conversations_are_normal_even_when_they_are_not_recent()
    {
        var parent = Record("parent", "Parent", ConversationCategory.Residual, "D:\\AI\\railway");
        var subagent = Record("subagent", "Subagent", ConversationCategory.Normal, "D:\\AI\\railway", isSubAgent: true);
        var sidebar = new FakeSidebar(new CodexProjectSidebarSnapshot(
        [
            new CodexProject("railway", "railway", ["D:\\AI\\railway"], 1)
        ],
        new Dictionary<string, string>(),
        new Dictionary<string, IReadOnlyList<string>>(),
        [],
        []));
        var viewModel = new MainViewModel(new FakeInventory([parent, subagent]), new SafeGuard(), projectSidebar: sidebar);

        await viewModel.RefreshAsync();

        Assert.Equal([parent.Id, subagent.Id], viewModel.VisibleRows.Select(row => row.Id).Order().ToArray());

        viewModel.SelectedCategory = ConversationCategory.Residual;

        Assert.Empty(viewModel.VisibleRows);
        Assert.Empty(viewModel.ProjectTree);

        viewModel.SelectedCategory = ConversationCategory.SubAgent;

        Assert.Equal([subagent.Id], viewModel.VisibleRows.Select(row => row.Id).ToArray());
    }

    [Fact]
    public async Task Orphaned_project_assignment_does_not_make_a_residual_conversation_normal()
    {
        var residual = Record("residual", "Residual", ConversationCategory.Residual);
        var sidebar = new FakeSidebar(new CodexProjectSidebarSnapshot(
            [],
            new Dictionary<string, string> { [residual.Id] = "unavailable-project" },
            new Dictionary<string, IReadOnlyList<string>>(),
            [],
            []));
        var viewModel = new MainViewModel(new FakeInventory([residual]), new SafeGuard(), projectSidebar: sidebar);

        await viewModel.RefreshAsync();

        Assert.Empty(viewModel.VisibleRows);

        viewModel.SelectedCategory = ConversationCategory.Residual;

        Assert.Equal([residual.Id], viewModel.VisibleRows.Select(row => row.Id).ToArray());
        Assert.Contains(viewModel.ProjectTree, node => node.Name == "残留对话");
    }

    [Fact]
    public async Task Residual_category_exposes_unassigned_residual_conversations_in_its_tree()
    {
        var residual = Record("residual", "Residual", ConversationCategory.Residual);
        var sidebar = new FakeSidebar(new CodexProjectSidebarSnapshot(
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            [],
            []));
        var viewModel = new MainViewModel(new FakeInventory([residual]), new SafeGuard(), projectSidebar: sidebar);

        await viewModel.RefreshAsync();
        viewModel.SelectedCategory = ConversationCategory.Residual;

        var residualNode = Assert.Single(viewModel.ProjectTree, node => node.Name == "残留对话");
        Assert.Equal([residual.Id], residualNode.Children.Select(node => node.Conversation!.Id).ToArray());

        viewModel.SelectedProjectNode = residualNode;

        Assert.Equal([residual.Id], viewModel.VisibleRows.Select(row => row.Id).ToArray());
    }

    [Fact]
    public async Task Damaged_category_exposes_index_only_conversations_in_its_tree()
    {
        var damaged = Record("index-only", "Index-only conversation", ConversationCategory.Damaged, null);
        var viewModel = new MainViewModel(new FakeInventory([damaged]), new SafeGuard());

        await viewModel.RefreshAsync();
        viewModel.SelectedCategory = ConversationCategory.Damaged;

        var damagedNode = Assert.Single(viewModel.ProjectTree, node => node.Name == "异常对话");
        Assert.Equal([damaged.Id], damagedNode.Children.Select(node => node.Conversation!.Id).ToArray());
    }

    [Fact]
    public void Conversation_row_exposes_the_original_session_and_index_source_paths()
    {
        var record = Record("paths", "Paths", ConversationCategory.Damaged) with
        {
            Evidence = ConversationEvidence.Empty("paths") with
            {
                ActiveSessionPaths = ["C:\\Users\\ASUS\\.codex\\sessions\\2026\\08\\17\\rollout-paths.jsonl"],
                SessionIndexPaths = ["C:\\Users\\ASUS\\.codex\\session_index.jsonl"]
            }
        };

        var row = new ConversationRowViewModel(record);

        Assert.Equal(
        [
            "C:\\Users\\ASUS\\.codex\\sessions\\2026\\08\\17\\rollout-paths.jsonl",
            "C:\\Users\\ASUS\\.codex\\session_index.jsonl"
        ], row.OriginalFilePaths);
    }

    [Fact]
    public async Task Selecting_the_current_category_marks_every_record_in_that_category_for_deletion()
    {
        var duplicate = Record("duplicate", "Duplicate", ConversationCategory.Duplicate);
        var normal = Record("normal", "Normal", ConversationCategory.Normal);
        var viewModel = new MainViewModel(new FakeInventory([duplicate, normal]), new SafeGuard());

        await viewModel.RefreshAsync();
        viewModel.SelectedCategory = ConversationCategory.Duplicate;
        viewModel.SelectVisibleRows();

        Assert.Equal(1, viewModel.SelectedCount);
        Assert.True(viewModel.CanStartDeletion);
        Assert.True(viewModel.Rows.Single(row => row.Id == duplicate.Id).IsSelected);
        Assert.False(viewModel.Rows.Single(row => row.Id == normal.Id).IsSelected);
    }

    [Fact]
    public async Task Changing_browse_scope_clears_hidden_selections_before_deletion()
    {
        var normal = Record("normal", "Normal", ConversationCategory.Normal);
        var duplicate = Record("duplicate", "Duplicate", ConversationCategory.Duplicate);
        var viewModel = new MainViewModel(new FakeInventory([normal, duplicate]), new SafeGuard());

        await viewModel.RefreshAsync();
        viewModel.SelectVisibleRows();
        viewModel.SelectedCategory = ConversationCategory.Duplicate;

        Assert.Equal(0, viewModel.SelectedCount);
        Assert.All(viewModel.Rows, row => Assert.False(row.IsSelected));
    }

    [Fact]
    public async Task Refresh_applies_local_results_before_background_app_server_reconciliation()
    {
        var local = Record("local", "Local", ConversationCategory.Normal);
        var appServer = Record("app-server", "App Server", ConversationCategory.Normal);
        var inventory = new LocalFirstInventory([local]);
        var viewModel = new MainViewModel(inventory, new SafeGuard());

        await viewModel.RefreshAsync();

        Assert.Equal(["local"], viewModel.Rows.Select(row => row.Id).ToArray());
        Assert.Contains("正在后台核对", viewModel.RefreshStatus);
        inventory.CompleteReconciliation([local, appServer]);
        await WaitUntilAsync(() => viewModel.Rows.Any(row => row.Id == appServer.Id));

        Assert.Equal(["app-server", "local"], viewModel.Rows.Select(row => row.Id).Order().ToArray());
        Assert.DoesNotContain("正在后台核对", viewModel.RefreshStatus);
    }

    [Fact]
    public async Task Refresh_ignores_a_stale_background_reconciliation_result()
    {
        var local = Record("local", "Local", ConversationCategory.Normal);
        var stale = Record("stale", "Stale", ConversationCategory.Normal);
        var current = Record("current", "Current", ConversationCategory.Normal);
        var inventory = new OutOfOrderLocalFirstInventory([local]);
        var viewModel = new MainViewModel(inventory, new SafeGuard());

        await viewModel.RefreshAsync();
        await viewModel.RefreshAsync();
        inventory.Complete(1, [local, current]);
        await WaitUntilAsync(() => viewModel.Rows.Any(row => row.Id == current.Id));
        inventory.Complete(0, [local, stale]);
        await Task.Delay(50);

        Assert.Contains(viewModel.Rows, row => row.Id == current.Id);
        Assert.DoesNotContain(viewModel.Rows, row => row.Id == stale.Id);
    }

    [Fact]
    public async Task Refresh_captures_the_ui_context_available_when_the_window_starts_refreshing()
    {
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var viewModel = new MainViewModel(new FakeInventory([]), new SafeGuard());
            var uiContext = new SynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(uiContext);

            await viewModel.RefreshAsync();

            var field = typeof(MainViewModel).GetField("_uiContext", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.Same(uiContext, field!.GetValue(viewModel));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task Background_app_server_titles_are_applied_through_the_window_dispatcher()
    {
        var local = Record("thread", "Local summary", ConversationCategory.Normal);
        var appServer = Record("thread", "Codex sidebar title", ConversationCategory.Normal);
        var inventory = new LocalFirstInventory([local]);
        var dispatcherCalls = 0;
        var viewModel = new MainViewModel(inventory, new SafeGuard(),
            uiDispatcher: action =>
            {
                dispatcherCalls++;
                action();
                return Task.CompletedTask;
            });

        await viewModel.RefreshAsync();
        inventory.CompleteReconciliation([appServer]);
        await WaitUntilAsync(() => viewModel.Rows.Single().Title == "Codex sidebar title");

        Assert.True(dispatcherCalls > 0);
    }

    private static ConversationRecord Record(string id, string title, ConversationCategory category, string? cwd = "D:\\work", bool isSubAgent = false) => new(
        id, title, category, "cli", cwd, null, null, category != ConversationCategory.Ghost,
        ConversationEvidence.Empty(id) with { ThreadSource = isSubAgent ? "subagent" : null });

    private sealed class FakeInventory(IReadOnlyList<ConversationRecord> records) : IConversationInventoryProvider
    {
        public Task<InventorySnapshot> RefreshAsync(InventoryMode mode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventorySnapshot(
                records,
                new Dictionary<string, string>(),
                Enum.GetValues<ConversationCategory>().ToDictionary(category => category, category => records.Count(record => record.Category == category)),
                []));
    }

    private sealed class LocalFirstInventory(IReadOnlyList<ConversationRecord> localRecords) : ILocalFirstConversationInventoryProvider
    {
        private readonly TaskCompletionSource<InventorySnapshot> _reconciliation = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<InventorySnapshot> RefreshAsync(InventoryMode mode, CancellationToken cancellationToken = default) =>
            _reconciliation.Task;

        public Task<InventorySnapshot> RefreshLocalAsync(InventoryMode mode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot(localRecords, pending: true));

        public Task<InventorySnapshot> ReconcileAppServerAsync(InventorySnapshot localSnapshot, InventoryMode mode, CancellationToken cancellationToken = default) =>
            _reconciliation.Task;

        public void CompleteReconciliation(IReadOnlyList<ConversationRecord> records) =>
            _reconciliation.TrySetResult(Snapshot(records, pending: false));

        private static InventorySnapshot Snapshot(IReadOnlyList<ConversationRecord> records, bool pending) => new(
            records, new Dictionary<string, string>(),
            Enum.GetValues<ConversationCategory>().ToDictionary(category => category, category => records.Count(record => record.Category == category)),
            [new InventoryDiagnostic("app-server-active", 0, DateTimeOffset.Now, null,
                pending ? InventoryReadStatus.Pending : InventoryReadStatus.Completed)]);
    }

    private sealed class OutOfOrderLocalFirstInventory(IReadOnlyList<ConversationRecord> localRecords) : ILocalFirstConversationInventoryProvider
    {
        private readonly List<TaskCompletionSource<InventorySnapshot>> _reconciliations = [];

        public Task<InventorySnapshot> RefreshAsync(InventoryMode mode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InventorySnapshot> RefreshLocalAsync(InventoryMode mode, CancellationToken cancellationToken = default) =>
            Task.FromResult(LocalFirstInventorySnapshot(localRecords, true));

        public Task<InventorySnapshot> ReconcileAppServerAsync(InventorySnapshot localSnapshot, InventoryMode mode, CancellationToken cancellationToken = default)
        {
            var result = new TaskCompletionSource<InventorySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            _reconciliations.Add(result);
            return result.Task;
        }

        public void Complete(int index, IReadOnlyList<ConversationRecord> records) =>
            _reconciliations[index].TrySetResult(LocalFirstInventorySnapshot(records, false));
    }

    private static InventorySnapshot LocalFirstInventorySnapshot(IReadOnlyList<ConversationRecord> records, bool pending) => new(
        records, new Dictionary<string, string>(),
        Enum.GetValues<ConversationCategory>().ToDictionary(category => category, category => records.Count(record => record.Category == category)),
        [new InventoryDiagnostic("app-server-active", 0, DateTimeOffset.Now, null,
            pending ? InventoryReadStatus.Pending : InventoryReadStatus.Completed)]);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for the background reconciliation result.");
    }

    private sealed class SafeGuard : IDeletionProcessGuard
    {
        public Task<ProcessGuardResult> CheckAsync(IReadOnlySet<int> ownedPids, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessGuardResult(true, []));
    }

    private sealed class BlockingGuard : IDeletionProcessGuard
    {
        public Task<ProcessGuardResult> CheckAsync(IReadOnlySet<int> ownedPids, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessGuardResult(false, [new ProcessSnapshot(1, "ChatGPT")]));
    }

    private sealed class RecordingGuard : IDeletionProcessGuard
    {
        public IReadOnlySet<int> OwnedPids { get; private set; } = new HashSet<int>();

        public Task<ProcessGuardResult> CheckAsync(IReadOnlySet<int> ownedPids, CancellationToken cancellationToken = default)
        {
            OwnedPids = ownedPids;
            return Task.FromResult(new ProcessGuardResult(true, []));
        }
    }

    private sealed class FakeDetails : IConversationDetailProvider
    {
        public Task<ConversationDetail> LoadAsync(ConversationRecord record, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConversationDetail(record.Id, ConversationDetailSource.AppServer,
                [new ConversationDetailBlock("user", "text", "Hello detail")]));
    }

    private sealed class FakeSidebar(CodexProjectSidebarSnapshot snapshot) : ICodexProjectSidebarProvider
    {
        public Task<CodexProjectSidebarSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

}
