namespace CodexConversationManager.Core.Domain;

public sealed record ConversationRecord(
    string Id,
    string DisplayTitle,
    ConversationCategory Category,
    string? SourceKind,
    string? Cwd,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool CanUseOfficialDelete,
    ConversationEvidence Evidence);
