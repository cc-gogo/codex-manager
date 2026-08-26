using CodexConversationManager.Core.Domain;

namespace CodexConversationManager.Core.Inventory;

public enum InventoryMode
{
    LiveCodex,
    Offline
}

public sealed record InventorySnapshot(
    IReadOnlyList<ConversationRecord> Records,
    IReadOnlyDictionary<string, string> SourceErrors,
    IReadOnlyDictionary<ConversationCategory, int> CategoryCounts,
    IReadOnlyList<InventoryDiagnostic> Diagnostics);

public interface IConversationInventoryProvider
{
    Task<InventorySnapshot> RefreshAsync(
        InventoryMode mode,
        CancellationToken cancellationToken = default);
}

public interface ILocalFirstConversationInventoryProvider : IConversationInventoryProvider
{
    Task<InventorySnapshot> RefreshLocalAsync(
        InventoryMode mode,
        CancellationToken cancellationToken = default);

    Task<InventorySnapshot> ReconcileAppServerAsync(
        InventorySnapshot localSnapshot,
        InventoryMode mode,
        CancellationToken cancellationToken = default);
}
