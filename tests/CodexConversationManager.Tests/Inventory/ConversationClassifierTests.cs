using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.Inventory;
using Xunit;

namespace CodexConversationManager.Tests.Inventory;

public sealed class ConversationClassifierTests
{
    private readonly ConversationClassifier _sut = new();

    [Fact]
    public void Recent_interactive_thread_is_normal()
    {
        var evidence = Complete("11111111-1111-7111-8111-111111111111") with { IsRecent = true };

        var result = _sut.Classify(evidence);

        Assert.Equal(ConversationCategory.Normal, result.Category);
        Assert.True(result.CanUseOfficialDelete);
    }

    [Fact]
    public void Archived_body_is_archived()
    {
        var evidence = Complete("22222222-2222-7222-8222-222222222222") with
        {
            IsArchived = true,
            ActiveSessionPaths = [],
            ArchivedSessionPaths = ["archived.jsonl"]
        };

        Assert.Equal(ConversationCategory.Archived, _sut.Classify(evidence).Category);
    }

    [Fact]
    public void Archived_subagent_is_archived()
    {
        var evidence = Complete("33333333-3333-7333-8333-333333333333") with
        {
            IsArchived = true,
            SourceKind = "subAgentReview",
            ThreadSource = "subagent"
        };

        Assert.Equal(ConversationCategory.Archived, _sut.Classify(evidence).Category);
    }

    [Fact]
    public void Catalog_only_id_is_an_abnormal_record()
    {
        var evidence = ConversationEvidence.Empty("44444444-4444-7444-8444-444444444444") with
        {
            CatalogRows = 1,
            GlobalReferenceCount = 2
        };

        var result = _sut.Classify(evidence);

        Assert.Equal(ConversationCategory.Damaged, result.Category);
        Assert.False(result.CanUseOfficialDelete);
    }

    [Fact]
    public void Session_index_only_id_is_an_abnormal_record()
    {
        var evidence = ConversationEvidence.Empty("45454545-4545-7454-8545-454545454545") with
        {
            SessionIndexRows = 1,
            Titles = ["Index title"]
        };

        Assert.Equal(ConversationCategory.Damaged, _sut.Classify(evidence).Category);
    }

    [Fact]
    public void State_without_readable_body_is_damaged()
    {
        var evidence = ConversationEvidence.Empty("55555555-5555-7555-8555-555555555555") with
        {
            StateRows = 1,
            CatalogRows = 1
        };

        Assert.Equal(ConversationCategory.Damaged, _sut.Classify(evidence).Category);
    }

    [Fact]
    public void Readable_local_conversation_without_codex_state_is_residual()
    {
        var evidence = ConversationEvidence.Empty("56565656-5656-7565-8565-565656565656") with
        {
            ActiveSessionPaths = ["sessions/residual.jsonl"],
            Titles = ["Residual"]
        };

        Assert.Equal(ConversationCategory.Residual, _sut.Classify(evidence).Category);
    }

    [Fact]
    public void Older_interactive_thread_outside_recent_list_is_residual()
    {
        var evidence = Complete("57575757-5757-7575-8575-575757575757");

        Assert.Equal(ConversationCategory.Residual, _sut.Classify(evidence).Category);
    }

    [Fact]
    public void Subagent_is_included_in_normal_conversations()
    {
        var evidence = Complete("58585858-5858-7585-8585-585858585858") with
        {
            SourceKind = "subAgentReview",
            ThreadSource = "subagent"
        };

        Assert.Equal(ConversationCategory.Normal, _sut.Classify(evidence).Category);
    }

    [Fact]
    public void Multiple_body_paths_are_duplicate_even_when_one_is_malformed()
    {
        var evidence = Complete("66666666-6666-7666-8666-666666666666") with
        {
            ActiveSessionPaths = ["one.jsonl", "two.jsonl"],
            ParseErrors = ["two.jsonl is malformed"]
        };

        Assert.Equal(ConversationCategory.Duplicate, _sut.Classify(evidence).Category);
    }

    [Fact]
    public void Parse_error_wins_over_ghost_references()
    {
        var evidence = ConversationEvidence.Empty("77777777-7777-7777-8777-777777777777") with
        {
            CatalogRows = 1,
            GlobalReferenceCount = 1,
            ParseErrors = ["invalid json"]
        };

        Assert.Equal(ConversationCategory.Damaged, _sut.Classify(evidence).Category);
    }

    private static ConversationEvidence Complete(string id) => ConversationEvidence.Empty(id) with
    {
        AppServerListed = true,
        AppServerReadable = true,
        ActiveSessionPaths = ["active.jsonl"],
        StateRows = 1,
        CatalogRows = 1,
        SourceKind = "vscode",
        Titles = ["Readable title"]
    };
}
