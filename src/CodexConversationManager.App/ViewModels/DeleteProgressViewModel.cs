using System.Collections.ObjectModel;
using CodexConversationManager.Core.Deletion;

namespace CodexConversationManager.App.ViewModels;

public sealed class DeleteProgressViewModel(
    IPermanentDeleteExecutor executor,
    DeletionPlan plan) : ObservableObject
{
    private int _completedCount;
    private bool _isRunning;

    public ObservableCollection<DeletionResult> Deleted { get; } = [];
    public ObservableCollection<DeletionResult> RewriteRisk { get; } = [];
    public ObservableCollection<DeleteProgressItem> Results { get; } = [];
    public ObservableCollection<DeletionResult> OfficialFailures { get; } = [];
    public ObservableCollection<DeletionResult> LocalFailures { get; } = [];
    public ObservableCollection<DeletionResult> ResidualFailures { get; } = [];
    public ObservableCollection<DeletionResult> DeletedByAncestor { get; } = [];

    public int CompletedCount
    {
        get => _completedCount;
        private set => SetProperty(ref _completedCount, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        try
        {
            var results = await executor.ExecuteAsync(plan, cancellationToken);
            foreach (var result in results)
            {
                switch (result.Status)
                {
                    case DeletionStatus.Deleted:
                        Deleted.Add(result);
                        break;
                    case DeletionStatus.DeletedWithRewriteRisk:
                        RewriteRisk.Add(result);
                        break;
                    case DeletionStatus.OfficialDeleteFailed:
                        OfficialFailures.Add(result);
                        break;
                    case DeletionStatus.LocalDeleteFailed:
                        LocalFailures.Add(result);
                        break;
                    case DeletionStatus.ResidualVerificationFailed:
                        ResidualFailures.Add(result);
                        break;
                    case DeletionStatus.DeletedByAncestor:
                        DeletedByAncestor.Add(result);
                        break;
                }

                Results.Add(DeleteProgressItem.From(result));

                CompletedCount++;
            }
        }
        finally
        {
            IsRunning = false;
        }
    }
}

public sealed record DeleteProgressItem(string Id, string Message)
{
    public static DeleteProgressItem From(DeletionResult result) => result.Status switch
    {
        DeletionStatus.Deleted => new(result.Id, "已删除"),
        DeletionStatus.DeletedWithRewriteRisk => new(result.Id, result.Error ?? "已删除，但 Codex 可能回写索引"),
        DeletionStatus.LocalDeleteFailed => new(result.Id, result.Error ?? "本地删除失败"),
        DeletionStatus.DeletedByAncestor => new(result.Id, "已随父对话删除"),
        _ => new(result.Id, result.Error ?? "删除未完成")
    };
}
