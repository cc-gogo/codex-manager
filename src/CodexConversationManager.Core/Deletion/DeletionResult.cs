namespace CodexConversationManager.Core.Deletion;

public enum DeletionStatus
{
    Deleted,
    DeletedWithRewriteRisk,
    OfficialDeleteFailed,
    LocalDeleteFailed,
    ResidualVerificationFailed,
    DeletedByAncestor
}

public sealed record DeletionResult(string Id, DeletionStatus Status, string? Error = null);

public interface IDeletionAppServer : IAsyncDisposable
{
    Task DeleteThreadAsync(string id, CancellationToken cancellationToken = default);
}

public interface IPermanentDeleteExecutor
{
    Task<IReadOnlyList<DeletionResult>> ExecuteAsync(
        DeletionPlan plan,
        CancellationToken cancellationToken = default);
}

public interface IGhostResidualCleaner
{
    Task CleanupAsync(string id, CancellationToken cancellationToken = default);
}

public interface ILocalThreadCleaner
{
    Task DeleteLocalThreadAsync(
        string id,
        IReadOnlyList<string> knownSessionPaths,
        CancellationToken cancellationToken = default);
}

public interface IResidualAuditor
{
    Task<bool> HasResidualsAsync(string id, CancellationToken cancellationToken = default);
}
