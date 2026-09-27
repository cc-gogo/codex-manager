using CodexConversationManager.Core.Inventory;
using CodexConversationManager.Core.LocalData;
using Xunit;

namespace CodexConversationManager.Tests.Acceptance;

public sealed class RealLocalInventoryReadOnlyTests
{
    [Fact]
    public async Task Current_codex_home_refreshes_all_local_state_threads_without_app_server()
    {
        if (Environment.GetEnvironmentVariable("CODEX_REAL_LOCAL_AUDIT") != "1") return;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var paths = CodexPaths.FromRoot(root);
        var stateRows = await new StateDatabaseReader(paths.StateDatabase).ReadThreadsAsync();
        var inventory = ReadOnlyConversationInventory.Create(root);

        var snapshot = await inventory.RefreshAsync(InventoryMode.LiveCodex);
        var sidebar = await new CodexProjectSidebarReader(paths.GlobalState, paths.StateDatabase).ReadAsync();

        Assert.All(stateRows, row => Assert.Contains(snapshot.Records, record => record.Id == row.Id));
        Assert.NotNull(sidebar.RecentThreadIds);
        Assert.Equal(sidebar.RecentThreadIds.Count,
            sidebar.RecentThreadIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Source.StartsWith("app-server", StringComparison.Ordinal));
        Assert.True(!snapshot.SourceErrors.Keys.Any(source => source is "sessions" or "state-db" or "catalog-db"),
            string.Join("; ", snapshot.SourceErrors.Select(pair => $"{pair.Key}: {pair.Value}")));
    }
}
