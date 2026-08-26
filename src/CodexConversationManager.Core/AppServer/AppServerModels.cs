using System.Text.Json.Nodes;

namespace CodexConversationManager.Core.AppServer;

public sealed record AppServerThread(string Id, JsonObject Raw);

public sealed record ThreadListResult(
    IReadOnlyList<AppServerThread> Threads,
    string? NextCursor);

public interface IConversationDetailReader
{
    Task<AppServerThread> ReadThreadAsync(
        string id,
        bool includeTurns,
        CancellationToken cancellationToken = default);
}

public sealed class AppServerProtocolException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class AppServerRpcException(int code, string message)
    : Exception($"App Server error {code}: {message}")
{
    public int Code { get; } = code;
}
