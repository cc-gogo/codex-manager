using CodexConversationManager.Core.Inventory;
using Xunit;

namespace CodexConversationManager.Tests.Mac;

public sealed class ReadOnlyConversationInventoryTests
{
    [Fact]
    public async Task Refresh_local_does_not_write_to_codex_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"codex-manager-readonly-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var before = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).ToArray();
        try
        {
            var inventory = ReadOnlyConversationInventory.Create(root);

            await inventory.RefreshLocalAsync(InventoryMode.LiveCodex);

            Assert.Equal(before, Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
