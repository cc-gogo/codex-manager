using System.Reflection;
using System.Text.Json.Nodes;
using CodexConversationManager.Core.Deletion;

namespace CodexConversationManager.Core.AppServer;

public sealed class CodexAppServerClient(
    IJsonRpcTransport transport,
    TimeSpan? requestTimeout = null) : IAppServerInventorySource, IDeletionAppServer, IConversationDetailReader
{
    private readonly TimeSpan _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
    private long _nextRequestId;
    private bool _initialized;

    public static IReadOnlyList<string> DefaultSourceKinds { get; } =
    [
        "cli", "vscode", "exec", "appServer", "subAgent", "subAgentReview",
        "subAgentCompact", "subAgentThreadSpawn", "subAgentOther", "unknown"
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            throw new InvalidOperationException("The App Server client is already initialized.");
        }

        var version = typeof(CodexAppServerClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(CodexAppServerClient).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var parameters = new JsonObject
        {
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "codex_conversation_manager",
                ["title"] = "Codex 对话管理器",
                ["version"] = version
            }
        };

        await RequestAsync("initialize", parameters, cancellationToken).ConfigureAwait(false);
        await transport.SendNotificationAsync(
            new JsonObject
            {
                ["method"] = "initialized",
                ["params"] = new JsonObject()
            },
            cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    public async Task<ThreadListResult> ListAllThreadsAsync(
        bool archived,
        bool useStateDbOnly,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var threads = new Dictionary<string, AppServerThread>(StringComparer.OrdinalIgnoreCase);
        string? cursor = null;

        do
        {
            var parameters = new JsonObject
            {
                ["cursor"] = cursor,
                ["limit"] = 100,
                ["archived"] = archived,
                ["useStateDbOnly"] = useStateDbOnly,
                ["sourceKinds"] = new JsonArray(
                    DefaultSourceKinds.Select(source => (JsonNode?)JsonValue.Create(source)).ToArray())
            };
            var result = await RequestAsync("thread/list", parameters, cancellationToken).ConfigureAwait(false)
                ?? throw new AppServerProtocolException("thread/list returned a null result.");
            var resultObject = result as JsonObject
                ?? throw new AppServerProtocolException("thread/list returned a non-object result.");
            if (resultObject["data"] is not JsonArray data)
            {
                throw new AppServerProtocolException("thread/list result is missing its data array.");
            }

            foreach (var node in data)
            {
                if (node is not JsonObject raw || raw["id"] is not JsonValue idValue ||
                    !idValue.TryGetValue<string>(out var id) || string.IsNullOrWhiteSpace(id))
                {
                    throw new AppServerProtocolException("thread/list returned a thread without an ID.");
                }

                threads.TryAdd(id, new AppServerThread(id, (JsonObject)raw.DeepClone()));
            }

            cursor = resultObject["nextCursor"]?.GetValue<string>();
        }
        while (cursor is not null);

        return new ThreadListResult(threads.Values.ToList(), null);
    }

    public async Task<AppServerThread> ReadThreadAsync(
        string id,
        bool includeTurns,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var result = await RequestAsync(
            "thread/read",
            new JsonObject { ["threadId"] = id, ["includeTurns"] = includeTurns },
            cancellationToken).ConfigureAwait(false);
        if (result?["thread"] is not JsonObject raw || raw["id"]?.GetValue<string>() is not { Length: > 0 } resultId)
        {
            throw new AppServerProtocolException("thread/read result is missing its thread.");
        }

        return new AppServerThread(resultId, (JsonObject)raw.DeepClone());
    }

    public async Task DeleteThreadAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await RequestAsync(
            "thread/delete",
            new JsonObject { ["threadId"] = id },
            cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonNode?> RequestAsync(
        string method,
        JsonObject parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        return transport.SendRequestAsync(
            new JsonObject
            {
                ["method"] = method,
                ["id"] = id,
                ["params"] = parameters
            },
            _requestTimeout,
            cancellationToken);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("InitializeAsync must complete before using the App Server client.");
        }
    }

    public ValueTask DisposeAsync() => transport.DisposeAsync();
}
