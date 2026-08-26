using System.IO;
using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Domain;

namespace CodexConversationManager.App.ViewModels;

public sealed class PermanentDeleteViewModel(DeletionPlan plan, IReadOnlyList<ConversationRecord>? records = null) : ObservableObject
{
    public int TargetCount { get; } = plan.OfficialDeleteRootIds
        .Concat(plan.GhostCleanupIds)
        .Concat(plan.DeletedByAncestorIds)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    public string WarningText
    {
        get
        {
            if (plan.BlockedByDescendantIds.Count > 0)
            {
                return $"已选的 {plan.BlockedByDescendantIds.Count} 条父对话包含子对话。为避免 Codex 连带删除未勾选的子对话，本次删除已阻止；请只勾选不包含子对话的会话。";
            }

            var sources = records?.SelectMany(record => SourceLabels(record.Evidence)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            return sources.Count == 0
                ? $"将永久删除 {TargetCount} 条对话；不保留备份，且无法恢复。"
                : $"将永久删除 {TargetCount} 条对话；不保留备份，且无法恢复。\n将检查并清理：{string.Join("、", sources)}。";
        }
    }

    private static IEnumerable<string> SourceLabels(ConversationEvidence evidence)
    {
        if (evidence.AppServerListed) yield return "App Server 记录";
        foreach (var path in evidence.ActiveSessionPaths.Concat(evidence.ArchivedSessionPaths))
            if (!string.IsNullOrWhiteSpace(path)) yield return Path.GetFileName(path);
        if (evidence.StateRows > 0) yield return "state-db";
        if (evidence.CatalogRows > 0) yield return "catalog-db";
        if (evidence.GlobalReferenceCount > 0) yield return "全局索引";
    }

    public bool CanConfirm => plan.BlockedByDescendantIds.Count == 0 && TargetCount > 0;
}
