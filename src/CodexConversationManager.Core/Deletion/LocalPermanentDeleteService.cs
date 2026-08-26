namespace CodexConversationManager.Core.Deletion;

public sealed class LocalPermanentDeleteService(
    ILocalThreadCleaner cleaner,
    IReadOnlyDictionary<string, IReadOnlyList<string>> sessionPathsById,
    bool codexMayRewriteIndexes = false) : IPermanentDeleteExecutor
{
    public async Task<IReadOnlyList<DeletionResult>> ExecuteAsync(
        DeletionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var targets = plan.OfficialDeleteRootIds
            .Concat(plan.GhostCleanupIds)
            .Concat(plan.DeletedByAncestorIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var results = new List<DeletionResult>();

        foreach (var id in targets)
        {
            try
            {
                var paths = sessionPathsById.TryGetValue(id, out var knownPaths) ? knownPaths : [];
                await cleaner.DeleteLocalThreadAsync(id, paths, cancellationToken).ConfigureAwait(false);
                results.Add(codexMayRewriteIndexes
                    ? new DeletionResult(id, DeletionStatus.DeletedWithRewriteRisk,
                        "已执行本地删除；Codex 正在运行，可能回写侧栏索引。完全退出 Codex 后再次删除可确保彻底清理。")
                    : new DeletionResult(id, DeletionStatus.Deleted));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new DeletionResult(id, DeletionStatus.LocalDeleteFailed, exception.Message));
            }
        }

        return results;
    }
}
