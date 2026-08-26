namespace CodexConversationManager.Core.Deletion;

public sealed class PermanentDeleteService(
    IDeletionProcessGuard processGuard,
    IDeletionAppServer appServer,
    IGhostResidualCleaner ghostCleaner,
    IResidualAuditor residualAuditor,
    IReadOnlySet<int> ownedPids) : IPermanentDeleteExecutor
{
    public async Task<IReadOnlyList<DeletionResult>> ExecuteAsync(
        DeletionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _ = processGuard;
        _ = ownedPids;
        _ = residualAuditor;
        var results = new List<DeletionResult>();
        try
        {
            foreach (var id in plan.OfficialDeleteRootIds)
            {
                try
                {
                    await appServer.DeleteThreadAsync(id, cancellationToken).ConfigureAwait(false);
                    results.Add(new DeletionResult(id, DeletionStatus.Deleted));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    results.Add(new DeletionResult(id, DeletionStatus.OfficialDeleteFailed, exception.Message));
                }
            }
        }
        finally
        {
            await appServer.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var id in plan.GhostCleanupIds)
        {
            try
            {
                await ghostCleaner.CleanupAsync(id, cancellationToken).ConfigureAwait(false);
                results.Add(new DeletionResult(id, DeletionStatus.Deleted));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new DeletionResult(id, DeletionStatus.ResidualVerificationFailed, exception.Message));
            }
        }

        results.AddRange(plan.DeletedByAncestorIds.Select(
            id => new DeletionResult(id, DeletionStatus.DeletedByAncestor)));
        return results.OrderBy(result => result.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
