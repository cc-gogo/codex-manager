using CodexConversationManager.Core.LocalData;

namespace CodexConversationManager.Core.Inventory;

public static class ReadOnlyConversationInventory
{
    public static ConversationInventoryService Create(string codexHome)
    {
        var paths = CodexPaths.FromRoot(Path.GetFullPath(codexHome));
        return new ConversationInventoryService(
            null,
            new SessionScanner(paths),
            new StateDatabaseReader(paths.StateDatabase),
            new CatalogDatabaseReader(paths.CatalogDatabase),
            new GlobalStateReader(paths.GlobalState),
            new ConversationClassifier(),
            new SessionIndexReader(Path.Combine(paths.Root, "session_index.jsonl")),
            new ThreadRelationshipDatabaseReader(paths.StateDatabase));
    }
}
