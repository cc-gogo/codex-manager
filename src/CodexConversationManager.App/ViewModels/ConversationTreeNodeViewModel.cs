using System.Collections.ObjectModel;

namespace CodexConversationManager.App.ViewModels;

public sealed class ConversationTreeNodeViewModel(
    string name,
    string? fullPath,
    bool isUnassigned = false,
    IReadOnlySet<string>? threadIds = null,
    bool isRecent = false,
    ConversationRowViewModel? conversation = null)
{
    public string Name { get; } = name;
    public string? FullPath { get; } = fullPath;
    public bool IsUnassigned { get; } = isUnassigned;
    public bool IsRecent { get; } = isRecent;
    public ConversationRowViewModel? Conversation { get; } = conversation;
    public bool IsConversation => Conversation is not null;
    public ObservableCollection<ConversationTreeNodeViewModel> Children { get; } = [];

    public void AddThread(string id)
    {
        if (threadIds is HashSet<string> ids)
        {
            ids.Add(id);
        }
    }

    public bool Matches(string cwd)
    {
        if (IsUnassigned)
        {
            return string.IsNullOrWhiteSpace(cwd);
        }

        if (string.IsNullOrWhiteSpace(FullPath) || string.IsNullOrWhiteSpace(cwd))
        {
            return false;
        }

        var normalizedCwd = NormalizePath(cwd);
        var normalizedNode = NormalizePath(FullPath);
        return string.Equals(normalizedCwd, normalizedNode, StringComparison.OrdinalIgnoreCase) ||
               normalizedCwd.StartsWith(normalizedNode + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesThread(string id) => threadIds is null || threadIds.Contains(id);

    internal static string NormalizePath(string path) =>
        path.Trim().Replace('/', '\\').TrimEnd('\\');
}
