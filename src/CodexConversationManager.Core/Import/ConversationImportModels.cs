namespace CodexConversationManager.Core.Import;

public enum DuplicateIdResolution
{
    Reject,
    GenerateNewId
}

public enum ImportProviderMode
{
    CurrentLogin,
    PreserveSource
}

public sealed record ConversationImportCandidate(
    string SourcePath,
    string SourceId,
    string TargetId,
    string Title,
    string Cwd,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string SourceProvider,
    string TargetProvider,
    bool HasDuplicateId);

public sealed record ConversationImportIssue(string SourcePath, string Message);

public sealed record ConversationImportPreview(
    IReadOnlyList<ConversationImportCandidate> Candidates,
    IReadOnlyList<ConversationImportIssue> Issues);

public interface IConversationImportPreviewService
{
    Task<ConversationImportPreview> PreviewAsync(
        IReadOnlyList<string> sourcePaths,
        string currentProvider,
        IReadOnlySet<string> existingIds,
        DuplicateIdResolution duplicateResolution,
        CancellationToken cancellationToken = default);
}

public interface IConversationImportService
{
    Task<ConversationImportResult> ApplyAsync(
        ConversationImportRequest request,
        CancellationToken cancellationToken = default);
}
