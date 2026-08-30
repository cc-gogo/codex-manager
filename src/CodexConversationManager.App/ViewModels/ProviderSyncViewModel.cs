using System.IO;
using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Sync;

namespace CodexConversationManager.App.ViewModels;

public sealed class ProviderSyncViewModel(ProviderSyncService service, IDeletionProcessGuard processGuard, IReadOnlySet<int> ownedPids, string? backupRoot = null) : ObservableObject
{
    private ProviderSyncPlan? _plan;
    private string _status = "正在读取当前登录模式";
    private bool _busy;
    private string _backupRoot = backupRoot ?? string.Empty;

    public ProviderSyncPlan? Plan { get => _plan; private set => SetProperty(ref _plan, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string BackupRoot { get => _backupRoot; private set => SetProperty(ref _backupRoot, value); }
    public bool CanApply => !_busy && Plan is { TotalCount: > 0 } && !string.IsNullOrWhiteSpace(BackupRoot);
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) OnPropertyChanged(nameof(CanApply)); } }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try { Plan = await service.PreviewAsync(); Status = Plan.TotalCount == 0 ? $"未发现需要同步的对话（目标 provider：{Plan.DestinationProvider}）。" : $"发现 {Plan.TotalCount} 条 provider 不一致的记录，将统一同步为 {Plan.DestinationProvider}。"; }
        catch (Exception ex) { Status = $"读取失败：{ex.Message}"; }
        finally { IsBusy = false; }
    }

    public async Task<bool> ApplyAsync()
    {
        if (!CanApply || Plan is null) return false;
        IsBusy = true;
        try
        {
            var guard = await processGuard.CheckAsync(ownedPids);
            if (!guard.IsSafe) { Status = "请先完全退出 Codex / ChatGPT 后再执行。"; return false; }
            var result = await service.ApplyAsync(Plan, BackupRoot);
            Status = $"已同步 {result.UpdatedCount} 条。备份：{result.BackupPath}";
            Plan = await service.PreviewAsync();
            return true;
        }
        catch (Exception ex) { Status = $"同步失败，已尝试恢复备份：{ex.Message}"; return false; }
        finally { IsBusy = false; }
    }

    public void SetBackupRoot(string path) => BackupRoot = Path.GetFullPath(path);
}
