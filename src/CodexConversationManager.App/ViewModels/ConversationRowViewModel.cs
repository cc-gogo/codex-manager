using CodexConversationManager.Core.Domain;

namespace CodexConversationManager.App.ViewModels;

public sealed class ConversationRowViewModel(ConversationRecord record) : ObservableObject
{
    private bool _isSelected;

    public ConversationRecord Record { get; } = record;
    public string Id => Record.Id;
    public string ShortId => Id.Length <= 12 ? Id : $"{Id[..8]}...{Id[^4..]}";
    public string Title => Record.DisplayTitle;
    public string SingleLineTitle => string.Join(" ", Title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    public string SourceSummary
    {
        get
        {
            var sources = new List<string>();
            if (Record.Evidence.AppServerListed) sources.Add("App Server");
            if (Record.Evidence.ActiveSessionPaths.Count > 0) sources.Add($"sessions {Record.Evidence.ActiveSessionPaths.Count}");
            if (Record.Evidence.ArchivedSessionPaths.Count > 0) sources.Add($"archived {Record.Evidence.ArchivedSessionPaths.Count}");
            if (Record.Evidence.StateRows > 0) sources.Add($"state-db {Record.Evidence.StateRows}");
            if (Record.Evidence.SessionIndexRows > 0) sources.Add($"session-index {Record.Evidence.SessionIndexRows}");
            if (Record.Evidence.CatalogRows > 0) sources.Add($"catalog-db {Record.Evidence.CatalogRows}");
            if (Record.Evidence.GlobalReferenceCount > 0) sources.Add($"global-index {Record.Evidence.GlobalReferenceCount}");
            return sources.Count == 0 ? "未找到本地来源" : string.Join(" · ", sources);
        }
    }
    public ConversationCategory Category => Record.Category;
    public bool IsSubAgent => Record.Evidence.IsSubAgent ||
                              Record.Evidence.SourceKind?.Contains("subagent", StringComparison.OrdinalIgnoreCase) == true ||
                              string.Equals(Record.Evidence.ThreadSource, "subagent", StringComparison.OrdinalIgnoreCase);
    public string Source => Record.SourceKind ?? "unknown";
    public string Cwd => Record.Cwd ?? string.Empty;
    public DateTimeOffset? UpdatedAt => Record.UpdatedAt ?? Record.CreatedAt;
    public IReadOnlyList<string> OriginalFilePaths => Record.Evidence.ActiveSessionPaths
        .Concat(Record.Evidence.ArchivedSessionPaths)
        .Concat(Record.Evidence.SessionIndexPaths)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    public string OriginalFilePathSummary
    {
        get
        {
            var bodyPaths = Record.Evidence.ActiveSessionPaths
                .Concat(Record.Evidence.ArchivedSessionPaths)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => $"正文文件：{path}");
            var indexPaths = Record.Evidence.SessionIndexPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => $"索引来源（未发现正文文件）：{path}");
            var lines = bodyPaths.Concat(indexPaths).ToList();
            return lines.Count == 0 ? "未找到本地文件路径" : string.Join(Environment.NewLine, lines);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
