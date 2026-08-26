using System.Collections.ObjectModel;
using CodexConversationManager.App.Services;
using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Import;

namespace CodexConversationManager.App.ViewModels;

public enum ImportDestinationKind
{
    Projectless,
    ExistingProject,
    NewProject
}

public sealed record ImportProjectOption(string Id, string Name);

public sealed class ConversationImportViewModel(
    IConversationImportPreviewService previewService,
    IConversationImportService importer,
    IDeletionProcessGuard processGuard,
    IReadOnlySet<int> ownedPids,
    IReadOnlySet<string> existingIds,
    IReadOnlyList<ImportProjectOption> projects,
    string currentProvider,
    Func<CancellationToken, Task<CodexRestartResult>>? stopCodex = null,
    Func<CancellationToken, Task<CodexRestartResult>>? restartCodex = null) : ObservableObject
{
    private readonly List<string> _sourcePaths = [];
    private string _status = "请选择要导入的 JSONL 文件";
    private string _selectedProjectId = projects.FirstOrDefault()?.Id ?? string.Empty;
    private string _newProjectParent = string.Empty;
    private string _newProjectName = string.Empty;
    private ImportDestinationKind _destinationKind = ImportDestinationKind.Projectless;
    private DuplicateIdResolution _duplicateResolution = DuplicateIdResolution.Reject;
    private ImportProviderMode _providerMode = ImportProviderMode.CurrentLogin;
    private bool _busy;
    private bool _imported;

    public ObservableCollection<ConversationImportCandidateViewModel> Candidates { get; } = [];
    public ObservableCollection<ConversationImportIssue> Issues { get; } = [];
    public IReadOnlyList<ImportProjectOption> Projects { get; } = projects;
    public string CurrentProvider { get; } = currentProvider;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string SelectedProjectId { get => _selectedProjectId; set { if (SetProperty(ref _selectedProjectId, value ?? string.Empty)) OnPropertyChanged(nameof(CanApply)); } }
    public string NewProjectParent { get => _newProjectParent; set { if (SetProperty(ref _newProjectParent, value ?? string.Empty)) OnPropertyChanged(nameof(CanApply)); } }
    public string NewProjectName { get => _newProjectName; set { if (SetProperty(ref _newProjectName, value ?? string.Empty)) OnPropertyChanged(nameof(CanApply)); } }
    public ImportDestinationKind DestinationKind { get => _destinationKind; set { if (SetProperty(ref _destinationKind, value)) OnPropertyChanged(nameof(CanApply)); } }
    public DuplicateIdResolution DuplicateResolution { get => _duplicateResolution; set { if (SetProperty(ref _duplicateResolution, value)) { OnPropertyChanged(nameof(CanApply)); _ = RefreshPreviewAsync(); } } }
    public ImportProviderMode ProviderMode { get => _providerMode; set { if (SetProperty(ref _providerMode, value)) OnPropertyChanged(nameof(CanApply)); } }
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) { OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(CanRestart)); } } }
    public bool CanRestart => _imported && !IsBusy && restartCodex is not null;
    public bool HasImported => _imported;
    public bool CanApply => !IsBusy && Candidates.Count > 0 && Issues.Count == 0 && DestinationIsValid;
    public bool DestinationIsValid => DestinationKind switch
    {
        ImportDestinationKind.Projectless => true,
        ImportDestinationKind.ExistingProject => !string.IsNullOrWhiteSpace(SelectedProjectId),
        ImportDestinationKind.NewProject => !string.IsNullOrWhiteSpace(NewProjectParent) && !string.IsNullOrWhiteSpace(NewProjectName),
        _ => false
    };

    public async Task LoadFilesAsync(IReadOnlyList<string> sourcePaths, CancellationToken cancellationToken = default)
    {
        _sourcePaths.Clear();
        _sourcePaths.AddRange(sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)));
        await RefreshPreviewAsync(cancellationToken);
    }

    public async Task RefreshPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (_sourcePaths.Count == 0) return;
        IsBusy = true;
        try
        {
            var preview = await previewService.PreviewAsync(_sourcePaths, CurrentProvider, existingIds, DuplicateResolution, cancellationToken);
            Candidates.Clear();
            Issues.Clear();
            foreach (var candidate in preview.Candidates) Candidates.Add(new ConversationImportCandidateViewModel(candidate));
            foreach (var issue in preview.Issues) Issues.Add(issue);
            Status = Issues.Count == 0 ? $"已识别 {Candidates.Count} 条可导入对话。" : $"发现 {Issues.Count} 个问题，请处理后再导入。";
            OnPropertyChanged(nameof(CanApply));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Candidates.Clear();
            Issues.Clear();
            Issues.Add(new ConversationImportIssue(string.Empty, exception.Message));
            Status = $"预览失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply) return false;
        IsBusy = true;
        try
        {
            var guard = await processGuard.CheckAsync(ownedPids, cancellationToken);
            if (!guard.IsSafe)
            {
                Status = "请先完全退出 Codex / ChatGPT 后再执行导入。";
                return false;
            }

            var destination = DestinationKind switch
            {
                ImportDestinationKind.Projectless => new ProjectlessDestination() as ImportDestination,
                ImportDestinationKind.ExistingProject => new ExistingProjectDestination(SelectedProjectId),
                ImportDestinationKind.NewProject => new NewProjectDestination(NewProjectParent, NewProjectName),
                _ => throw new InvalidOperationException("未选择导入目标。")
            };
            var result = await importer.ApplyAsync(new ConversationImportRequest(
                new ConversationImportPreview(Candidates.Select(candidate => candidate.ToCandidate()).ToList(), Issues.ToList()), destination, ProviderMode), cancellationToken);
            Status = $"已导入 {result.ImportedCount} 条。备份：{result.BackupPath}。请重启 Codex 使左侧列表刷新。";
            _imported = true;
            OnPropertyChanged(nameof(CanRestart));
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = $"导入失败：{exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> StopCodexAsync(CancellationToken cancellationToken = default)
    {
        if (stopCodex is null) return false;
        IsBusy = true;
        try
        {
            var result = await stopCodex(cancellationToken);
            var guard = await processGuard.CheckAsync(ownedPids, cancellationToken);
            Status = guard.IsSafe
                ? result.Warnings.Count == 0 ? "已退出 Codex，可以开始导入。" : $"已尝试退出 Codex，但有 {result.Warnings.Count} 个进程未能关闭。"
                : "仍检测到 Codex 进程，请点击重新检查或手动退出。";
            return guard.IsSafe;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = $"退出 Codex 失败：{exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> CheckCodexExitAsync(CancellationToken cancellationToken = default)
    {
        var guard = await processGuard.CheckAsync(ownedPids, cancellationToken);
        Status = guard.IsSafe ? "已确认 Codex 已退出，可以开始导入。" : "仍检测到 Codex 进程，请先退出。";
        return guard.IsSafe;
    }

    public async Task<bool> RestartCodexAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRestart || restartCodex is null) return false;
        IsBusy = true;
        try
        {
            var result = await restartCodex(cancellationToken);
            Status = result.Warnings.Count == 0 ? "Codex 正在重新启动。" : $"Codex 已请求重启，但有 {result.Warnings.Count} 个进程未能关闭。";
            return result.Warnings.Count == 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = $"重启 Codex 失败：{exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
