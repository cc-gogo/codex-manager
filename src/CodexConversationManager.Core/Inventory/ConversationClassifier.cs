using CodexConversationManager.Core.Domain;

namespace CodexConversationManager.Core.Inventory;

public sealed class ConversationClassifier
{
    public ConversationRecord Classify(ConversationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (string.IsNullOrWhiteSpace(evidence.Id))
        {
            throw new ArgumentException("Conversation ID is required.", nameof(evidence));
        }

        var activePaths = evidence.ActiveSessionPaths.Where(NotBlank);
        var archivedPaths = evidence.ArchivedSessionPaths.Where(NotBlank);
        var bodyPathCount = activePaths.Concat(archivedPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var hasReadableBody = evidence.AppServerReadable || bodyPathCount > 0;
        var hasState = evidence.StateRows > 0;
        var hasReferences = evidence.CatalogRows > 0 || evidence.GlobalReferenceCount > 0;
        var isIndexOnly = evidence.SessionIndexRows > 0 && !hasReadableBody && !hasState && !hasReferences;

        var category = bodyPathCount > 1
            ? ConversationCategory.Duplicate
            : evidence.ParseErrors.Count > 0 || isIndexOnly || (hasState && !hasReadableBody)
                ? ConversationCategory.Damaged
                : !hasReadableBody && !hasState && hasReferences
                    ? ConversationCategory.Damaged
                    : evidence.IsArchived || archivedPaths.Any()
                            ? ConversationCategory.Archived
                            : evidence.IsSubAgent || IsSubAgent(evidence) || evidence.IsRecent
                                ? ConversationCategory.Normal
                                : ConversationCategory.Residual;

        var title = evidence.Titles.FirstOrDefault(NotBlank)?.Trim() ?? evidence.Id;
        return new ConversationRecord(
            evidence.Id,
            title,
            category,
            evidence.SourceKind,
            evidence.Cwd,
            evidence.CreatedAt,
            evidence.UpdatedAt,
            evidence.AppServerListed || evidence.AppServerReadable,
            evidence);
    }

    private static bool IsSubAgent(ConversationEvidence evidence) =>
        evidence.SourceKind?.StartsWith("subAgent", StringComparison.OrdinalIgnoreCase) == true ||
        string.Equals(evidence.ThreadSource, "subagent", StringComparison.OrdinalIgnoreCase);

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);
}
