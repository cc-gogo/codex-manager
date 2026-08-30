namespace CodexConversationManager.Core.LocalData;

public sealed record CodexProject(string Id, string Name, IReadOnlyList<string> RootPaths, int Order);
public sealed record CodexThreadSection(string Id, string Name);

public sealed record CodexProjectSidebarSnapshot(
    IReadOnlyList<CodexProject> Projects,
    IReadOnlyDictionary<string, string> ThreadProjectIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SidebarThreadOrders,
    IReadOnlyList<string>? ProjectlessThreadIds = null,
    IReadOnlyList<string>? RecentThreadIds = null)
{
    public IReadOnlyList<string>? ArchivedRecentThreadIds { get; init; }
    public IReadOnlyList<string> PinnedThreadIds { get; init; } = [];
    public IReadOnlyDictionary<string, string> ThreadSectionIds { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<CodexThreadSection> ThreadSections { get; init; } = [];

    public static CodexProjectSidebarSnapshot Empty { get; } = new([], new Dictionary<string, string>(), new Dictionary<string, IReadOnlyList<string>>(), [], []);
}

public interface ICodexProjectSidebarProvider
{
    Task<CodexProjectSidebarSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}
