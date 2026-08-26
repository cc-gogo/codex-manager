namespace CodexConversationManager.Core.Deletion;

public sealed record DeletionPlan(
    IReadOnlyList<string> OfficialDeleteRootIds,
    IReadOnlyList<string> GhostCleanupIds,
    IReadOnlyList<string> DeletedByAncestorIds)
{
    // Codex's thread/delete cascades to descendants, so parent targets require an explicit safe workflow.
    public IReadOnlyList<string> BlockedByDescendantIds { get; init; } = [];
}
