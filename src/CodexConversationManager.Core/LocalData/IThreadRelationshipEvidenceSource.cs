namespace CodexConversationManager.Core.LocalData;

public sealed record ThreadRelationshipEvidence(string ParentId, string ChildId);

public interface IThreadRelationshipEvidenceSource
{
    Task<IReadOnlyList<ThreadRelationshipEvidence>> ReadAsync(CancellationToken cancellationToken = default);
}
