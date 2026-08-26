namespace CodexConversationManager.Core.Domain;

public sealed record ConversationEvidence
{
    public required string Id { get; init; }
    public bool AppServerListed { get; init; }
    public bool AppServerReadable { get; init; }
    public bool IsRecent { get; init; }
    public bool IsSubAgent { get; init; }
    public bool IsArchived { get; init; }
    public IReadOnlyList<string> ActiveSessionPaths { get; init; } = [];
    public IReadOnlyList<string> ArchivedSessionPaths { get; init; } = [];
    public int StateRows { get; init; }
    public int SessionIndexRows { get; init; }
    public IReadOnlyList<string> SessionIndexPaths { get; init; } = [];
    public int CatalogRows { get; init; }
    public int GlobalReferenceCount { get; init; }
    public string? SourceKind { get; init; }
    public string? ThreadSource { get; init; }
    public IReadOnlyList<string> ParseErrors { get; init; } = [];
    public IReadOnlyList<string> Titles { get; init; } = [];
    public string? Cwd { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public IReadOnlyList<string> DescendantIds { get; init; } = [];

    public static ConversationEvidence Empty(string id) => new() { Id = id };
}
