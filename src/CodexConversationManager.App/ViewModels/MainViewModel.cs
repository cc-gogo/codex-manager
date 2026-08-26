using System.Collections.ObjectModel;
using System.IO;
using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.Inventory;
using CodexConversationManager.Core.LocalData;

namespace CodexConversationManager.App.ViewModels;

public sealed class MainViewModel(
    IConversationInventoryProvider inventory,
    IDeletionProcessGuard processGuard,
    IReadOnlySet<int>? ownedAppServerPids = null,
    IConversationDetailProvider? detailProvider = null,
    ICodexProjectSidebarProvider? projectSidebar = null,
    Func<Action, Task>? uiDispatcher = null) : ObservableObject
{
    private readonly IReadOnlySet<int> _ownedAppServerPids = ownedAppServerPids ?? new HashSet<int>();
    private readonly Func<Action, Task>? _uiDispatcher = uiDispatcher;
    private string _searchText = string.Empty;
    private ConversationCategory? _selectedCategory = ConversationCategory.Normal;
    private bool _canDelete;
    private string _deletionStatus = "正在检查删除安全状态";
    private string _refreshStatus = "尚未读取";
    private ConversationRowViewModel? _selectedRow;
    private ConversationTreeNodeViewModel? _selectedProjectNode;
    private string _detailStatus = "请选择一条对话以查看详情";
    private int _selectedCount;
    private CancellationTokenSource? _detailLoadCancellation;
    private IReadOnlyList<InventoryDiagnostic> _refreshDiagnostics = [];
    private SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private long _refreshGeneration;

    public ObservableCollection<ConversationRowViewModel> Rows { get; } = [];

    public ObservableCollection<ConversationTreeNodeViewModel> ProjectTree { get; } = [];

    public IReadOnlyList<ConversationRowViewModel> VisibleRows => Rows
        .Where(row => MatchesSelectedCategory(row) &&
                      (string.IsNullOrWhiteSpace(_searchText) ||
                       row.Id.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                       row.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                       row.Cwd.Contains(_searchText, StringComparison.OrdinalIgnoreCase)) &&
                      (_selectedProjectNode is null ||
                       (_selectedProjectNode.MatchesThread(row.Id) &&
                        (_selectedProjectNode.IsRecent || _selectedProjectNode.IsUnassigned || _selectedProjectNode.FullPath is null || _selectedProjectNode.Matches(row.Cwd)))))
        .ToList();

    public ConversationTreeNodeViewModel? SelectedProjectNode
    {
        get => _selectedProjectNode;
        set
        {
            if (SetProperty(ref _selectedProjectNode, value))
            {
                ClearSelections();
                OnPropertyChanged(nameof(VisibleRows));
                OnPropertyChanged(nameof(CanSelectVisibleRows));
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ClearSelections();
                OnPropertyChanged(nameof(VisibleRows));
                OnPropertyChanged(nameof(CanSelectVisibleRows));
            }
        }
    }

    public ConversationCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ClearSelections();
                SelectedProjectNode = null;
                RebuildProjectTree();
                NotifyCategorySelectionChanged();
                OnPropertyChanged(nameof(VisibleRows));
                OnPropertyChanged(nameof(CanSelectVisibleRows));
                OnPropertyChanged(nameof(SelectVisibleButtonText));
            }
        }
    }

    public bool IsNormalCategorySelected => _selectedCategory == ConversationCategory.Normal;
    public bool IsSubAgentCategorySelected => _selectedCategory == ConversationCategory.SubAgent;
    public bool IsResidualCategorySelected => _selectedCategory == ConversationCategory.Residual;
    public bool IsArchivedCategorySelected => _selectedCategory == ConversationCategory.Archived;
    public bool IsDamagedCategorySelected => _selectedCategory == ConversationCategory.Damaged;
    public bool IsDuplicateCategorySelected => _selectedCategory == ConversationCategory.Duplicate;

    public bool CanDelete
    {
        get => _canDelete;
        private set => SetProperty(ref _canDelete, value);
    }

    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            if (SetProperty(ref _selectedCount, value))
            {
                OnPropertyChanged(nameof(CanStartDeletion));
                OnPropertyChanged(nameof(DeleteButtonText));
            }
        }
    }

    public bool CanStartDeletion => CanDelete && SelectedCount > 0;

    public bool CanSelectVisibleRows => VisibleRows.Any(row => !row.IsSelected);

    public string DeleteButtonText => SelectedCount == 0 ? "请先勾选左侧对话" : $"永久删除已选对话（{SelectedCount}）";

    public string SelectVisibleButtonText => SelectedCategory is null ? "全选当前列表" : "全选当前分类";

    public void SelectVisibleRows()
    {
        foreach (var row in VisibleRows)
        {
            row.IsSelected = true;
        }

        OnPropertyChanged(nameof(CanSelectVisibleRows));
    }

    private void ClearSelections()
    {
        foreach (var row in Rows.Where(row => row.IsSelected).ToList())
        {
            row.IsSelected = false;
        }

        OnPropertyChanged(nameof(CanSelectVisibleRows));
    }

    public string DeletionStatus
    {
        get => _deletionStatus;
        private set => SetProperty(ref _deletionStatus, value);
    }

    public string RefreshStatus
    {
        get => _refreshStatus;
        private set => SetProperty(ref _refreshStatus, value);
    }

    public IReadOnlyList<InventoryDiagnostic> RefreshDiagnostics
    {
        get => _refreshDiagnostics;
        private set => SetProperty(ref _refreshDiagnostics, value);
    }

    public ConversationRowViewModel? SelectedRow
    {
        get => _selectedRow;
        private set => SetProperty(ref _selectedRow, value);
    }

    public IReadOnlyList<ConversationDetailBlock> DetailBlocks { get; private set; } = [];

    public string DetailStatus
    {
        get => _detailStatus;
        private set => SetProperty(ref _detailStatus, value);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _uiContext ??= SynchronizationContext.Current;
        RefreshStatus = "正在读取对话目录";
        var generation = Interlocked.Increment(ref _refreshGeneration);
        var localFirst = inventory as ILocalFirstConversationInventoryProvider;
        var snapshot = localFirst is null
            ? await inventory.RefreshAsync(InventoryMode.LiveCodex, cancellationToken)
            : await localFirst.RefreshLocalAsync(InventoryMode.LiveCodex, cancellationToken);
        _projectSidebar = projectSidebar is null
            ? CodexProjectSidebarSnapshot.Empty
            : await projectSidebar.ReadAsync(cancellationToken);
        ApplySnapshot(snapshot, localFirst is not null);

        if (localFirst is not null)
        {
            _ = ReconcileAppServerAsync(localFirst, snapshot, generation, cancellationToken);
        }

        var guard = await processGuard.CheckAsync(_ownedAppServerPids, cancellationToken);
        CanDelete = true;
        OnPropertyChanged(nameof(CanStartDeletion));
        DeletionStatus = guard.IsSafe
            ? "可以直接永久删除；删除后请重启 Codex，以使左侧列表生效。"
            : "Codex 当前正在运行，可以直接删除；删除完成后请重启 Codex，以使左侧列表生效。";
    }

    private void ApplySnapshot(InventorySnapshot snapshot, bool isReconciling)
    {
        Rows.Clear();
        foreach (var record in snapshot.Records)
        {
            var row = new ConversationRowViewModel(record);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ConversationRowViewModel.IsSelected))
                    SelectedCount = Rows.Count(item => item.IsSelected);
            };
            Rows.Add(row);
        }
        SelectedCount = 0;

        RebuildProjectTree();
        RefreshDiagnostics = snapshot.Diagnostics;
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(CanSelectVisibleRows));
        OnPropertyChanged(nameof(SelectVisibleButtonText));
        RefreshStatus = isReconciling
            ? $"已读取 {Rows.Count} 条对话；App Server 正在后台核对"
            : snapshot.SourceErrors.Count == 0
            ? $"已读取 {Rows.Count} 条对话"
            : $"已读取 {Rows.Count} 条对话，{snapshot.SourceErrors.Count} 个来源发生错误";
    }

    private async Task ReconcileAppServerAsync(
        ILocalFirstConversationInventoryProvider localFirst,
        InventorySnapshot localSnapshot,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var reconciled = await localFirst.ReconcileAppServerAsync(
                localSnapshot, InventoryMode.LiveCodex, cancellationToken).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                if (generation != Volatile.Read(ref _refreshGeneration)) return;
                ApplySnapshot(reconciled, false);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await RunOnUiAsync(() =>
            {
                if (generation == Volatile.Read(ref _refreshGeneration))
                    RefreshStatus = $"本地结果已显示；App Server 核对失败：{exception.Message}";
            }).ConfigureAwait(false);
        }
    }

    private Task RunOnUiAsync(Action action)
    {
        if (_uiDispatcher is not null)
        {
            return _uiDispatcher(action);
        }

        if (_uiContext is null || ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(_ =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }, null);
        return completion.Task;
    }

    private void RebuildProjectTree()
    {
        ProjectTree.Clear();
        var eligibleRows = Rows.Where(MatchesSelectedCategory).ToList();
        var prioritizeArchivedRecent = _selectedCategory == ConversationCategory.Archived;
        var recentThreadIds = prioritizeArchivedRecent
            ? _projectSidebar.ArchivedRecentThreadIds ?? []
            : _projectSidebar.RecentThreadIds ?? [];
        var projectIdByThread = eligibleRows.ToDictionary(row => row.Id, ResolveProjectId, StringComparer.OrdinalIgnoreCase);
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in _projectSidebar.Projects)
        {
            var projectRows = eligibleRows.Where(row =>
                projectIdByThread.TryGetValue(row.Id, out var projectId) &&
                string.Equals(projectId, project.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (projectRows.Count > 0)
            {
                var projectNode = new ConversationTreeNodeViewModel(project.Name, null,
                    threadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                foreach (var row in projectRows)
                {
                    projectNode.AddThread(row.Id);
                    AddProjectFolder(projectNode, project, row);
                    assigned.Add(row.Id);
                }

                SortChildren(projectNode);
                ProjectTree.Add(projectNode);
            }
        }

        var rowsById = eligibleRows.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        var recentRows = recentThreadIds
            .Where(id => !assigned.Contains(id))
            .Where(id => prioritizeArchivedRecent || !_projectSidebar.ThreadProjectIds.ContainsKey(id))
            .Where(rowsById.ContainsKey)
            .Select(id => rowsById[id])
            .ToList();
        if (recentRows.Count > 0)
        {
            var recent = new ConversationTreeNodeViewModel(prioritizeArchivedRecent ? "最近文件夹" : "最近对话", null,
                threadIds: recentRows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase), isRecent: true);
            foreach (var row in recentRows) AddConversationNode(recent, row);
            ProjectTree.Add(recent);
        }

        var subagentRows = eligibleRows
            .Where(row => row.IsSubAgent && !assigned.Contains(row.Id) && !recentRows.Contains(row))
            .OrderByDescending(row => row.UpdatedAt)
            .ToList();
        if (subagentRows.Count > 0)
        {
            var subagents = new ConversationTreeNodeViewModel("子代理对话", null,
                threadIds: subagentRows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
            AddConversationNodes(subagents, subagentRows);
            ProjectTree.Add(subagents);
        }

        var residualRows = eligibleRows
            .Where(row => row.Category == ConversationCategory.Residual &&
                          !row.IsSubAgent &&
                          !assigned.Contains(row.Id) &&
                          !recentRows.Contains(row))
            .OrderByDescending(row => row.UpdatedAt)
            .ToList();
        if (_selectedCategory == ConversationCategory.Residual && residualRows.Count > 0)
        {
            var residual = new ConversationTreeNodeViewModel("残留对话", null,
                threadIds: residualRows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
            AddConversationNodes(residual, residualRows);
            ProjectTree.Add(residual);
        }

        if (_selectedCategory is { } selectedCategory &&
            selectedCategory is not ConversationCategory.Normal and not ConversationCategory.SubAgent and not ConversationCategory.Residual)
        {
            var unassignedRows = eligibleRows
                .Where(row => !assigned.Contains(row.Id) && !recentRows.Contains(row) && !subagentRows.Contains(row))
                .OrderByDescending(row => row.UpdatedAt)
                .ToList();
            if (unassignedRows.Count > 0)
            {
                var unassigned = new ConversationTreeNodeViewModel(CategoryDisplayName(selectedCategory), null,
                    threadIds: unassignedRows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
                AddConversationNodes(unassigned, unassignedRows);
                ProjectTree.Add(unassigned);
            }
        }
    }

    private CodexProjectSidebarSnapshot _projectSidebar = CodexProjectSidebarSnapshot.Empty;

    private bool MatchesSelectedCategory(ConversationRowViewModel row) => _selectedCategory switch
    {
        null => true,
        ConversationCategory.Normal => !IsArchived(row) &&
            (row.Category == ConversationCategory.Normal || row.IsSubAgent || IsCodexSidebarConversation(row)),
        ConversationCategory.SubAgent => row.IsSubAgent,
        ConversationCategory.Residual => row.Category == ConversationCategory.Residual && !IsCodexSidebarConversation(row),
        _ => row.Category == _selectedCategory
    };

    private bool IsCodexSidebarConversation(ConversationRowViewModel row) =>
        ResolveProjectId(row) is not null ||
        (!_projectSidebar.ThreadProjectIds.ContainsKey(row.Id) &&
         (_projectSidebar.RecentThreadIds ?? []).Contains(row.Id, StringComparer.OrdinalIgnoreCase));

    private static bool IsArchived(ConversationRowViewModel row) =>
        row.Category == ConversationCategory.Archived || row.Record.Evidence.IsArchived;

    private void NotifyCategorySelectionChanged()
    {
        OnPropertyChanged(nameof(IsNormalCategorySelected));
        OnPropertyChanged(nameof(IsSubAgentCategorySelected));
        OnPropertyChanged(nameof(IsResidualCategorySelected));
        OnPropertyChanged(nameof(IsArchivedCategorySelected));
        OnPropertyChanged(nameof(IsDamagedCategorySelected));
        OnPropertyChanged(nameof(IsDuplicateCategorySelected));
    }

    private string? ResolveProjectId(ConversationRowViewModel row)
    {
        if (_projectSidebar.ThreadProjectIds.TryGetValue(row.Id, out var id) &&
            _projectSidebar.Projects.Any(project => string.Equals(project.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return id;
        }

        var sidebarProjectId = _projectSidebar.SidebarThreadOrders
            .Where(pair => pair.Value.Contains(row.Id, StringComparer.OrdinalIgnoreCase))
            .Select(pair => _projectSidebar.Projects.FirstOrDefault(project =>
                string.Equals(project.Id, pair.Key, StringComparison.OrdinalIgnoreCase)))
            .Where(project => project is not null)
            .OrderBy(project => project!.Order)
            .ThenBy(project => project!.Id, StringComparer.OrdinalIgnoreCase)
            .Select(project => project!.Id)
            .FirstOrDefault();
        if (sidebarProjectId is not null)
        {
            return sidebarProjectId;
        }

        return _projectSidebar.Projects
            .SelectMany(project => project.RootPaths
                .Where(root => IsPathUnder(row.Cwd, root))
                .Select(root => new { project, RootLength = ConversationTreeNodeViewModel.NormalizePath(root).Length }))
            .OrderByDescending(candidate => candidate.RootLength)
            .ThenBy(candidate => candidate.project.Order)
            .ThenBy(candidate => candidate.project.Id, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.project.Id)
            .FirstOrDefault() as string;
    }

    private static void AddProjectFolder(
        ConversationTreeNodeViewModel projectNode,
        CodexProject project,
        ConversationRowViewModel row)
    {
        var root = project.RootPaths
            .OrderByDescending(path => path.Length)
            .FirstOrDefault(path => IsPathUnder(row.Cwd, path));
        if (root is null || string.IsNullOrWhiteSpace(row.Cwd)) { AddConversationNode(projectNode, row); return; }

        var relative = Path.GetRelativePath(root, row.Cwd).Replace('/', '\\');
        if (relative is "." or "" || relative.StartsWith("..", StringComparison.Ordinal)) { AddConversationNode(projectNode, row); return; }

        var current = projectNode;
        var currentPath = root;
        foreach (var segment in relative.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            var next = current.Children.FirstOrDefault(child =>
                string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                next = new ConversationTreeNodeViewModel(segment, currentPath,
                    threadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                current.Children.Add(next);
            }

            next.AddThread(row.Id);
            current = next;
        }

        AddConversationNode(current, row);
    }

    private static void AddConversationNodes(ConversationTreeNodeViewModel parent, IEnumerable<ConversationRowViewModel> rows)
    {
        foreach (var row in rows.OrderByDescending(row => row.UpdatedAt)) AddConversationNode(parent, row);
    }

    private static void AddConversationNode(ConversationTreeNodeViewModel parent, ConversationRowViewModel row) =>
        parent.Children.Add(new ConversationTreeNodeViewModel(row.SingleLineTitle, null,
            threadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.Id }, conversation: row));

    private static string CategoryDisplayName(ConversationCategory category) => category switch
    {
        ConversationCategory.Archived => "归档对话",
        ConversationCategory.Damaged => "异常对话",
        ConversationCategory.Duplicate => "重复对话",
        _ => category.ToString()
    };

    private static bool IsPathUnder(string cwd, string root) =>
        !string.IsNullOrWhiteSpace(cwd) &&
        (string.Equals(ConversationTreeNodeViewModel.NormalizePath(cwd), ConversationTreeNodeViewModel.NormalizePath(root), StringComparison.OrdinalIgnoreCase) ||
         ConversationTreeNodeViewModel.NormalizePath(cwd).StartsWith(ConversationTreeNodeViewModel.NormalizePath(root) + "\\", StringComparison.OrdinalIgnoreCase));

    private static void SortChildren(ConversationTreeNodeViewModel node)
    {
        var sorted = node.Children.OrderBy(child => child.IsConversation).ThenBy(child => child.IsConversation ? string.Empty : child.Name, StringComparer.OrdinalIgnoreCase).ToList();
        node.Children.Clear();
        foreach (var child in sorted)
        {
            SortChildren(child);
            node.Children.Add(child);
        }
    }

    public Task<ConversationDetail> LoadDetailAsync(
        ConversationRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (detailProvider is null)
        {
            throw new InvalidOperationException("详情读取服务不可用。");
        }

        return detailProvider.LoadAsync(row.Record, cancellationToken);
    }

    public async Task SelectAsync(ConversationRowViewModel? row, CancellationToken cancellationToken = default)
    {
        _detailLoadCancellation?.Cancel();
        _detailLoadCancellation?.Dispose();
        _detailLoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var detailCancellation = _detailLoadCancellation.Token;
        SelectedRow = row;
        DetailBlocks = [];
        OnPropertyChanged(nameof(DetailBlocks));
        if (row is null)
        {
            DetailStatus = "请选择一条对话以查看详情";
            return;
        }

        if (detailProvider is null)
        {
            DetailStatus = "详情读取服务不可用";
            return;
        }

        DetailStatus = "正在读取详情";
        try
        {
            var detail = await detailProvider.LoadAsync(row.Record, detailCancellation);
            if (!ReferenceEquals(row, SelectedRow))
            {
                return;
            }

            DetailBlocks = detail.Blocks;
            OnPropertyChanged(nameof(DetailBlocks));
            DetailStatus = detail.Source == ConversationDetailSource.AppServer
                ? "详情来自 Codex App Server"
                : "详情来自本地会话文件";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DetailStatus = $"无法读取详情：{exception.Message}";
        }
        catch (OperationCanceledException) when (detailCancellation.IsCancellationRequested)
        {
        }
    }
}
