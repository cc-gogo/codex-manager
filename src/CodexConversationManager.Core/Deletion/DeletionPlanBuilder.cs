using CodexConversationManager.Core.Domain;

namespace CodexConversationManager.Core.Deletion;

public sealed class DeletionPlanBuilder
{
    public DeletionPlan Build(IReadOnlyList<ConversationRecord> selectedRecords)
    {
        ArgumentNullException.ThrowIfNull(selectedRecords);
        var selected = selectedRecords
            .GroupBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(record => record.Id, StringComparer.OrdinalIgnoreCase);
        var blockedByDescendants = selected.Values
            .Where(record => record.Evidence.DescendantIds.Count > 0)
            .Select(record => record.Id)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var blockedIds = blockedByDescendants.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Codex's thread/delete cascades to every descendant. Never turn a selected parent into
        // an executable target because the hierarchy is also rendered in multiple browser views.
        var roots = selected.Values
            .Where(record => record.Category != ConversationCategory.Ghost)
            .Where(record => !blockedIds.Contains(record.Id))
            .Select(record => record.Id)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ghostCleanup = selected.Values
            .Where(record => record.Category == ConversationCategory.Ghost)
            .Where(record => !blockedIds.Contains(record.Id))
            .Select(record => record.Id)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new DeletionPlan(roots, ghostCleanup, [])
        {
            BlockedByDescendantIds = blockedByDescendants
        };
    }
}
