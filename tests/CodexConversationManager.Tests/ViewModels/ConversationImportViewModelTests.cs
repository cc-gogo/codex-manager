using CodexConversationManager.App.ViewModels;
using CodexConversationManager.Core.Deletion;
using CodexConversationManager.Core.Import;
using CodexConversationManager.App.Services;
using Xunit;

namespace CodexConversationManager.Tests.ViewModels;

public sealed class ConversationImportViewModelTests
{
    [Fact]
    public async Task Apply_refuses_to_write_when_codex_is_running()
    {
        var importer = new RecordingImporter();
        var viewModel = new ConversationImportViewModel(
            new StubPreviewService(), importer, new BlockingGuard(), new HashSet<int>(),
            new HashSet<string>(), [], "openai");
        await viewModel.LoadFilesAsync(["D:\\import.jsonl"]);
        viewModel.DestinationKind = ImportDestinationKind.Projectless;

        var applied = await viewModel.ApplyAsync();

        Assert.False(applied);
        Assert.Contains("完全退出 Codex", viewModel.Status);
        Assert.False(importer.Called);
    }

    [Fact]
    public async Task StopCodex_invokes_the_close_only_action_and_updates_status()
    {
        var stopped = false;
        var viewModel = new ConversationImportViewModel(
            new StubPreviewService(), new RecordingImporter(), new SafeGuard(), new HashSet<int>(),
            new HashSet<string>(), [], "openai", _ =>
            {
                stopped = true;
                return Task.FromResult(new CodexRestartResult([]));
            });

        await viewModel.StopCodexAsync();

        Assert.True(stopped);
        Assert.Contains("已退出", viewModel.Status);
    }

    [Fact]
    public async Task RestartCodex_is_available_only_after_import_and_invokes_restart_action()
    {
        var restarted = false;
        var viewModel = new ConversationImportViewModel(
            new StubPreviewService(), new SuccessfulImporter(), new SafeGuard(), new HashSet<int>(),
            new HashSet<string>(), [], "openai", null, _ =>
            {
                restarted = true;
                return Task.FromResult(new CodexRestartResult([]));
            });

        Assert.False(viewModel.CanRestart);
        await viewModel.LoadFilesAsync(["D:\\import.jsonl"]);
        Assert.True(await viewModel.ApplyAsync());

        Assert.True(viewModel.CanRestart);
        await viewModel.RestartCodexAsync();
        Assert.True(restarted);
    }

    [Fact]
    public async Task Apply_uses_title_edited_in_preview()
    {
        var importer = new CapturingImporter();
        var viewModel = new ConversationImportViewModel(
            new StubPreviewService(), importer, new SafeGuard(), new HashSet<int>(),
            new HashSet<string>(), [], "openai");

        await viewModel.LoadFilesAsync(["D:\\import.jsonl"]);
        viewModel.Candidates[0].Title = "我给它起的新名字";

        Assert.True(await viewModel.ApplyAsync());
        Assert.Equal("我给它起的新名字", Assert.Single(importer.Request!.Preview.Candidates).Title);
    }

    private sealed class StubPreviewService : IConversationImportPreviewService
    {
        public Task<ConversationImportPreview> PreviewAsync(IReadOnlyList<string> sourcePaths, string currentProvider,
            IReadOnlySet<string> existingIds, DuplicateIdResolution duplicateResolution, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConversationImportPreview(
            [new ConversationImportCandidate("D:\\import.jsonl", "11111111-1111-7111-8111-111111111111", "11111111-1111-7111-8111-111111111111", "Imported", "D:\\imported", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "openai", "openai", false)], []));
    }

    private sealed class RecordingImporter : IConversationImportService
    {
        public bool Called { get; private set; }

        public Task<ConversationImportResult> ApplyAsync(ConversationImportRequest request, CancellationToken cancellationToken = default)
        {
            Called = true;
            throw new InvalidOperationException("Should not be called");
        }
    }

    private sealed class SuccessfulImporter : IConversationImportService
    {
        public Task<ConversationImportResult> ApplyAsync(ConversationImportRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConversationImportResult(["D:\\imported.jsonl"], "D:\\backup", 1));
    }

    private sealed class CapturingImporter : IConversationImportService
    {
        public ConversationImportRequest? Request { get; private set; }

        public Task<ConversationImportResult> ApplyAsync(ConversationImportRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ConversationImportResult(["D:\\imported.jsonl"], "D:\\backup", 1));
        }
    }

    private sealed class BlockingGuard : IDeletionProcessGuard
    {
        public Task<ProcessGuardResult> CheckAsync(IReadOnlySet<int> ownedPids, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessGuardResult(false, [new ProcessSnapshot(1, "Codex")]));
    }

    private sealed class SafeGuard : IDeletionProcessGuard
    {
        public Task<ProcessGuardResult> CheckAsync(IReadOnlySet<int> ownedPids, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessGuardResult(true, []));
    }
}
