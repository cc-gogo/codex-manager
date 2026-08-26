using CodexConversationManager.App.ViewModels;
using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Domain;
using Xunit;

namespace CodexConversationManager.Tests.ViewModels;

public sealed class PermanentDeleteViewModelTests
{
    [Fact]
    public void Confirmation_is_ready_without_requiring_the_user_to_type_a_phrase()
    {
        var viewModel = new PermanentDeleteViewModel(new DeletionPlan(["parent"], ["ghost"], ["child"]));

        Assert.True(viewModel.CanConfirm);
        Assert.Contains("3", viewModel.WarningText);
        Assert.Contains("不保留备份", viewModel.WarningText);
    }

    [Fact]
    public void Confirmation_is_disabled_when_a_selected_parent_has_descendants()
    {
        var viewModel = new PermanentDeleteViewModel(new DeletionPlan([], [], [])
        {
            BlockedByDescendantIds = ["parent"]
        });

        Assert.False(viewModel.CanConfirm);
        Assert.Contains("子对话", viewModel.WarningText);
    }

    [Fact]
    public void Warning_lists_local_sources_for_review()
    {
        var record = new ConversationRecord("thread-1", "Title", ConversationCategory.Normal, "cli", "D:\\work", null, null, true,
            new ConversationEvidence
            {
                Id = "thread-1", AppServerListed = true, StateRows = 1, CatalogRows = 1,
                ActiveSessionPaths = ["D:\\codex\\sessions\\rollout.jsonl"]
            });

        var viewModel = new PermanentDeleteViewModel(new DeletionPlan(["thread-1"], [], []), [record]);

        Assert.Contains("rollout.jsonl", viewModel.WarningText);
        Assert.Contains("state-db", viewModel.WarningText);
    }

    [Fact]
    public async Task Batch_progress_continues_and_groups_all_result_statuses()
    {
        var executor = new FakeExecutor(
        [
            new DeletionResult("ok", DeletionStatus.Deleted),
            new DeletionResult("failed", DeletionStatus.OfficialDeleteFailed, "failure"),
            new DeletionResult("residual", DeletionStatus.ResidualVerificationFailed, "residual"),
            new DeletionResult("child", DeletionStatus.DeletedByAncestor)
        ]);
        var viewModel = new DeleteProgressViewModel(executor, new DeletionPlan(["ok", "failed"], ["residual"], ["child"]));

        await viewModel.ExecuteAsync();

        Assert.Equal(4, viewModel.CompletedCount);
        Assert.Single(viewModel.Deleted);
        Assert.Single(viewModel.OfficialFailures);
        Assert.Single(viewModel.ResidualFailures);
        Assert.Single(viewModel.DeletedByAncestor);
        Assert.Equal(4, viewModel.Results.Count);
        Assert.Contains(viewModel.Results, item => item.Id == "ok" && item.Message == "已删除");
    }

    private sealed class FakeExecutor(IReadOnlyList<DeletionResult> results) : IPermanentDeleteExecutor
    {
        public Task<IReadOnlyList<DeletionResult>> ExecuteAsync(DeletionPlan plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(results);
    }
}
