using System.Text.Json.Nodes;
using CodexConversationManager.Core.AppServer;
using Xunit;

namespace CodexConversationManager.Tests.AppServer;

public sealed class CodexAppServerClientTests
{
    [Fact]
    public async Task Initialize_request_precedes_initialized_notification_and_omits_jsonrpc()
    {
        await using var transport = new InMemoryTransport([JsonNode.Parse("""{"platformFamily":"windows"}""")]);
        await using var client = new CodexAppServerClient(transport);

        await client.InitializeAsync();

        Assert.Collection(
            transport.Sent,
            request =>
            {
                Assert.Equal("initialize", request["method"]?.GetValue<string>());
                Assert.NotNull(request["id"]);
                Assert.Null(request["jsonrpc"]);
                Assert.Equal("codex_conversation_manager", request["params"]?["clientInfo"]?["name"]?.GetValue<string>());
                Assert.Equal("Codex 对话管理器", request["params"]?["clientInfo"]?["title"]?.GetValue<string>());
            },
            notification =>
            {
                Assert.Equal("initialized", notification["method"]?.GetValue<string>());
                Assert.Null(notification["id"]);
                Assert.Null(notification["jsonrpc"]);
            });
    }

    [Fact]
    public async Task Thread_list_follows_all_cursors_and_deduplicates_ids()
    {
        await using var transport = new InMemoryTransport(
        [
            JsonNode.Parse("""{}"""),
            JsonNode.Parse("""{"data":[{"id":"a"},{"id":"b"}],"nextCursor":"page-2"}"""),
            JsonNode.Parse("""{"data":[{"id":"b"},{"id":"c"}],"nextCursor":"page-3"}"""),
            JsonNode.Parse("""{"data":[{"id":"d"}],"nextCursor":null}""")
        ]);
        await using var client = new CodexAppServerClient(transport);
        await client.InitializeAsync();

        var result = await client.ListAllThreadsAsync(archived: false, useStateDbOnly: true);

        Assert.Equal(["a", "b", "c", "d"], result.Threads.Select(thread => thread.Id));
        Assert.Null(result.NextCursor);
        var listRequests = transport.Sent.Where(message => message["method"]?.GetValue<string>() == "thread/list").ToList();
        Assert.Equal(3, listRequests.Count);
        Assert.Null(listRequests[0]["params"]?["cursor"]);
        Assert.Equal("page-2", listRequests[1]["params"]?["cursor"]?.GetValue<string>());
        Assert.Equal("page-3", listRequests[2]["params"]?["cursor"]?.GetValue<string>());
        Assert.All(listRequests, request =>
        {
            Assert.True(request["params"]?["useStateDbOnly"]?.GetValue<bool>());
            Assert.False(request["params"]?["archived"]?.GetValue<bool>());
            Assert.Equal(CodexAppServerClient.DefaultSourceKinds, request["params"]?["sourceKinds"]?.AsArray().Select(x => x!.GetValue<string>()));
        });
    }

    [Fact]
    public async Task Read_and_delete_use_exact_thread_id()
    {
        const string id = "019fd5b1-a888-7801-ab5b-6f1bbba8663f";
        await using var transport = new InMemoryTransport(
        [
            JsonNode.Parse("""{}"""),
            new JsonObject
            {
                ["thread"] = new JsonObject
                {
                    ["id"] = id,
                    ["turns"] = new JsonArray()
                }
            },
            JsonNode.Parse("""{}""")
        ]);
        await using var client = new CodexAppServerClient(transport);
        await client.InitializeAsync();

        var thread = await client.ReadThreadAsync(id, includeTurns: true);
        await client.DeleteThreadAsync(id);

        Assert.Equal(id, thread.Id);
        var read = Assert.Single(transport.Sent, x => x["method"]?.GetValue<string>() == "thread/read");
        Assert.Equal(id, read["params"]?["threadId"]?.GetValue<string>());
        Assert.True(read["params"]?["includeTurns"]?.GetValue<bool>());
        var delete = Assert.Single(transport.Sent, x => x["method"]?.GetValue<string>() == "thread/delete");
        Assert.Equal(id, delete["params"]?["threadId"]?.GetValue<string>());
    }

    [Fact]
    public async Task Client_rejects_calls_before_initialization()
    {
        await using var transport = new InMemoryTransport([]);
        await using var client = new CodexAppServerClient(transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ListAllThreadsAsync(archived: false, useStateDbOnly: true));
    }

    private sealed class InMemoryTransport(IEnumerable<JsonNode?> responses) : IJsonRpcTransport
    {
        private readonly Queue<JsonNode?> _responses = new(responses);

        public List<JsonObject> Sent { get; } = [];

        public Task<JsonNode?> SendRequestAsync(
            JsonObject request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((JsonObject)request.DeepClone());
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No fake response was configured.");
            }

            return Task.FromResult(_responses.Dequeue()?.DeepClone());
        }

        public Task SendNotificationAsync(
            JsonObject notification,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((JsonObject)notification.DeepClone());
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
