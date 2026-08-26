namespace CodexConversationManager.Core.LocalData;

public sealed record SessionEvidence(
    string Id,
    string Path,
    bool IsArchived,
    string? SourceKind,
    string? ThreadSource,
    string? Cwd,
    DateTimeOffset? CreatedAt,
    string? ParseError);

public sealed record StateThreadEvidence(
    string Id,
    string RolloutPath,
    string SourceKind,
    string? ThreadSource,
    string Cwd,
    string Title,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? RecencyAt = null);

public sealed record CatalogThreadEvidence(
    string Id,
    string HostId,
    string DisplayTitle,
    string SourceKind,
    string? ThreadSource,
    string Cwd,
    bool IsMissingCandidate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SessionIndexEvidence(string Id, string Title, DateTimeOffset? UpdatedAt, string? SourcePath = null);

public sealed record GlobalStateReference(string Id, string JsonPath);
