namespace CodexConversationManager.Core.LocalData;

public sealed record CodexPaths(
    string Root,
    string Sessions,
    string ArchivedSessions,
    string StateDatabase,
    string CatalogDatabase,
    string GlobalState)
{
    public static CodexPaths FromRoot(string absoluteRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteRoot);
        if (!Path.IsPathFullyQualified(absoluteRoot))
        {
            throw new ArgumentException("Codex root must be an absolute path.", nameof(absoluteRoot));
        }

        var root = Path.GetFullPath(absoluteRoot);
        return new CodexPaths(
            root,
            Path.Combine(root, "sessions"),
            Path.Combine(root, "archived_sessions"),
            Path.Combine(root, "state_5.sqlite"),
            Path.Combine(root, "sqlite", "codex-dev.db"),
            Path.Combine(root, ".codex-global-state.json"));
    }
}
