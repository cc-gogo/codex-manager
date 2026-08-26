using CodexConversationManager.Core.Import;

namespace CodexConversationManager.App.ViewModels;

public sealed class ConversationImportCandidateViewModel : ObservableObject
{
    private readonly ConversationImportCandidate _candidate;
    private string _title = string.Empty;

    public ConversationImportCandidateViewModel(ConversationImportCandidate candidate)
    {
        _candidate = candidate;
        _title = candidate.Title;
    }

    public ConversationImportCandidate Candidate => _candidate;
    public string SourcePath => _candidate.SourcePath;
    public string SourceId => _candidate.SourceId;
    public string TargetId => _candidate.TargetId;
    public string Cwd => _candidate.Cwd;
    public DateTimeOffset CreatedAt => _candidate.CreatedAt;
    public DateTimeOffset UpdatedAt => _candidate.UpdatedAt;
    public string SourceProvider => _candidate.SourceProvider;
    public string TargetProvider => _candidate.TargetProvider;
    public bool HasDuplicateId => _candidate.HasDuplicateId;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty);
    }

    public ConversationImportCandidate ToCandidate() => Candidate with
    {
        Title = string.IsNullOrWhiteSpace(Title) ? _candidate.Title : Title.Trim()
    };
}
