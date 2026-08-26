using System.Text.Json;
using CodexConversationManager.Core.AppServer;
using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.Inventory;
using CodexConversationManager.Core.LocalData;
using Xunit;

namespace CodexConversationManager.Tests.Acceptance;

public sealed class RealInventoryReadOnlyTests
{
    private const string RegressionIdOne = "019fd5b1-a888-7801-ab5b-6f1bbba8663f";
    private const string RegressionIdTwo = "019fd5c9-a9aa-7862-adf1-30a3319239cb";
    private const string SubagentRegressionId = "019f7094-4f23-7443-aef0-0e8f679d3bac";

    [Fact]
    public async Task Real_local_inventory_keeps_the_subagent_marker_for_the_known_subagent_thread()
    {
        if (Environment.GetEnvironmentVariable("CODEX_REAL_INVENTORY") != "1")
        {
            return;
        }

        var paths = CodexPaths.FromRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
        await using var transport = new StdioJsonRpcTransport(CodexExecutableLocator.Locate());
        await using var client = new CodexAppServerClient(transport, TimeSpan.FromSeconds(30));
        await client.InitializeAsync();
        var service = new ConversationInventoryService(
            client,
            new SessionScanner(paths),
            new StateDatabaseReader(paths.StateDatabase),
            new CatalogDatabaseReader(paths.CatalogDatabase),
            new GlobalStateReader(paths.GlobalState),
            new ConversationClassifier());

        var record = (await service.RefreshAsync(InventoryMode.LiveCodex)).Records
            .Single(record => record.Id == SubagentRegressionId);

        Assert.Equal("subagent", record.Evidence.ThreadSource);
        Assert.Equal(ConversationCategory.Normal, record.Category);
    }

    [Fact]
    public async Task Real_inventory_contains_both_known_missing_threads_without_writes()
    {
        if (Environment.GetEnvironmentVariable("CODEX_REAL_INVENTORY") != "1")
        {
            return;
        }

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var paths = CodexPaths.FromRoot(root);
        await using var transport = new StdioJsonRpcTransport(CodexExecutableLocator.Locate());
        await using var client = new CodexAppServerClient(transport, TimeSpan.FromSeconds(30));
        await client.InitializeAsync();
        var service = new ConversationInventoryService(
            client,
            new SessionScanner(paths),
            new StateDatabaseReader(paths.StateDatabase),
            new CatalogDatabaseReader(paths.CatalogDatabase),
            new GlobalStateReader(paths.GlobalState),
            new ConversationClassifier());

        var snapshot = await service.RefreshAsync(InventoryMode.LiveCodex);
        var firstPresent = snapshot.Records.Any(record => record.Id == RegressionIdOne);
        var secondPresent = snapshot.Records.Any(record => record.Id == RegressionIdTwo);
        var productRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var logPath = Path.Combine(productRoot, "logs", "real-inventory-readonly.json");
        await File.WriteAllTextAsync(logPath, JsonSerializer.Serialize(new
        {
            total = snapshot.Records.Count,
            categories = snapshot.CategoryCounts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            sourceErrorCount = snapshot.SourceErrors.Count,
            regressionIdOnePresent = firstPresent,
            regressionIdTwoPresent = secondPresent
        }));

        Assert.True(firstPresent);
        Assert.True(secondPresent);
    }
}
