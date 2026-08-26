using System.Text.Json.Nodes;
using CodexConversationManager.Core.AppServer;
using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.Inventory;
using Xunit;

namespace CodexConversationManager.Tests.Inventory;

public sealed class ConversationDetailServiceTests
{
    [Fact]
    public async Task App_server_detail_is_preferred_and_returns_structured_blocks()
    {
        var reader = new FakeDetailReader(new AppServerThread("11111111-1111-7111-8111-111111111111", new JsonObject
        {
            ["id"] = "11111111-1111-7111-8111-111111111111",
            ["turns"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["items"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = "Hello from App Server" }
                    }
                }
            }
        }));
        var service = new ConversationDetailService(reader);

        var detail = await service.LoadAsync(CreateRecord());

        Assert.Equal(ConversationDetailSource.AppServer, detail.Source);
        var block = Assert.Single(detail.Blocks);
        Assert.Equal("user", block.Role);
        Assert.Equal("text", block.Kind);
        Assert.Equal("Hello from App Server", block.Text);
        Assert.True(reader.IncludeTurnsRequested);
    }

    [Fact]
    public async Task Jsonl_is_used_as_bounded_fallback_when_app_server_cannot_read()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "detail-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "rollout-11111111-1111-7111-8111-111111111111.jsonl");
        await File.WriteAllTextAsync(path, """
            {"type":"session_meta","payload":{"id":"11111111-1111-7111-8111-111111111111"}}
            {"type":"event_msg","payload":{"type":"user_message","message":"Fallback user text"}}
            {"type":"event_msg","payload":{"type":"assistant_message","message":"Fallback assistant text"}}
            """);
        try
        {
            var service = new ConversationDetailService(new ThrowingDetailReader());
            var detail = await service.LoadAsync(CreateRecord(path));

            Assert.Equal(ConversationDetailSource.SessionFile, detail.Source);
            Assert.Collection(detail.Blocks,
                block => Assert.Equal(("user", "Fallback user text"), (block.Role, block.Text)),
                block => Assert.Equal(("assistant", "Fallback assistant text"), (block.Role, block.Text)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Jsonl_response_items_are_read_without_waiting_for_the_app_server()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "detail-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "rollout-11111111-1111-7111-8111-111111111111.jsonl");
        await File.WriteAllTextAsync(path, """
            {"type":"session_meta","payload":{"id":"11111111-1111-7111-8111-111111111111"}}
            {"type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"Modern user text"}]}}
            {"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Modern assistant text"}]}}
            """);
        try
        {
            var service = new ConversationDetailService(new ThrowingDetailReader());

            var detail = await service.LoadAsync(CreateRecord(path));

            Assert.Equal(ConversationDetailSource.SessionFile, detail.Source);
            Assert.Collection(detail.Blocks,
                block => Assert.Equal(("user", "Modern user text"), (block.Role, block.Text)),
                block => Assert.Equal(("assistant", "Modern assistant text"), (block.Role, block.Text)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ConversationRecord CreateRecord(string? sessionPath = null) => new(
        "11111111-1111-7111-8111-111111111111",
        "test",
        ConversationCategory.Normal,
        null,
        null,
        null,
        null,
        true,
        ConversationEvidence.Empty("11111111-1111-7111-8111-111111111111") with
        {
            ActiveSessionPaths = sessionPath is null ? [] : [sessionPath]
        });

    private sealed class FakeDetailReader(AppServerThread thread) : IConversationDetailReader
    {
        public bool IncludeTurnsRequested { get; private set; }

        public Task<AppServerThread> ReadThreadAsync(string id, bool includeTurns, CancellationToken cancellationToken = default)
        {
            IncludeTurnsRequested = includeTurns;
            return Task.FromResult(thread);
        }
    }

    private sealed class ThrowingDetailReader : IConversationDetailReader
    {
        public Task<AppServerThread> ReadThreadAsync(string id, bool includeTurns, CancellationToken cancellationToken = default) =>
            Task.FromException<AppServerThread>(new AppServerProtocolException("unavailable"));
    }
}
