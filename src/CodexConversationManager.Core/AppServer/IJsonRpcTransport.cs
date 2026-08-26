using System.Text.Json.Nodes;

namespace CodexConversationManager.Core.AppServer;

public interface IJsonRpcTransport : IAsyncDisposable
{
    Task<JsonNode?> SendRequestAsync(
        JsonObject request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task SendNotificationAsync(
        JsonObject notification,
        CancellationToken cancellationToken = default);
}
