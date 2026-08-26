namespace CodexConversationManager.Core.AppServer;

public interface IAppServerInventorySource
{
    Task<ThreadListResult> ListAllThreadsAsync(
        bool archived,
        bool useStateDbOnly,
        CancellationToken cancellationToken = default);
}
