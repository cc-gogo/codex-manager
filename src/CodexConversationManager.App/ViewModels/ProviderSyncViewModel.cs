using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Sync;

namespace CodexConversationManager.App.ViewModels;

public sealed class ProviderSyncViewModel(ProviderSyncService service, IDeletionProcessGuard processGuard, IReadOnlySet<int> ownedPids) : ObservableObject
{
    private ProviderSyncPlan? _plan;
    private string _status = "正在读取当前登录模式";
    private string _confirmation = string.Empty;
    private bool _busy;

    public ProviderSyncPlan? Plan { get => _plan; private set => SetProperty(ref _plan, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Confirmation { get => _confirmation; set { if (SetProperty(ref _confirmation, value)) OnPropertyChanged(nameof(CanApply)); } }
    public bool CanApply => !_busy && Plan is { TotalCount: > 0 } && Confirmation == "同步 provider";
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) OnPropertyChanged(nameof(CanApply)); } }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try { Plan = await service.PreviewAsync(); Status = Plan.TotalCount == 0 ? $"未发现需要同步的 {Plan.SourceProvider} 对话。" : $"发现 {Plan.TotalCount} 条记录将从 {Plan.SourceProvider} 同步为 {Plan.DestinationProvider}。"; }
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
            var result = await service.ApplyAsync(Plan);
            Status = $"已同步 {result.UpdatedCount} 条。备份：{result.BackupPath}";
            Plan = await service.PreviewAsync();
            Confirmation = string.Empty;
            return true;
        }
        catch (Exception ex) { Status = $"同步失败，已尝试恢复备份：{ex.Message}"; return false; }
        finally { IsBusy = false; }
    }
}
