namespace CodexConversationManager.Core.Sync;

public sealed record ProviderSyncTarget(string Path, string Kind, int Count);

public sealed record ProviderSyncPlan(
    string SourceProvider,
    string DestinationProvider,
    IReadOnlyList<ProviderSyncTarget> Targets)
{
    public int TotalCount => Targets.Sum(target => target.Count);
}

public sealed record ProviderSyncResult(
    ProviderSyncPlan Plan,
    string BackupPath,
    int UpdatedCount);
