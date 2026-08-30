using System.Text.Json.Nodes;
using CodexConversationManager.Core.Import;
using Xunit;

namespace CodexConversationManager.Tests.Import;

public sealed class ConversationImportPreviewServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CodexConversationManagerTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Preview_reads_session_meta_and_does_not_modify_source_file()
    {
        var id = "11111111-1111-7111-8111-111111111111";
        var path = await WriteJsonlAsync("rollout.jsonl", ValidRollout(id));
        var before = await File.ReadAllTextAsync(path);

        var preview = await new ConversationImportPreviewService().PreviewAsync(
            [path], "openai", new HashSet<string>(), DuplicateIdResolution.Reject);

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal(id, candidate.SourceId);
        Assert.Equal(id, candidate.TargetId);
        Assert.Equal("Imported conversation", candidate.Title);
        Assert.Equal("openai", candidate.TargetProvider);
        Assert.Equal(before, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Preview_rejects_jsonl_without_a_valid_session_meta_uuid()
    {
        var path = await WriteJsonlAsync("invalid.jsonl", "{\"type\":\"event_msg\"}\n");

        var preview = await new ConversationImportPreviewService().PreviewAsync(
            [path], "openai", new HashSet<string>(), DuplicateIdResolution.Reject);

        Assert.Empty(preview.Candidates);
        Assert.Contains(preview.Issues, issue => issue.Message.Contains("session_meta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_generates_a_new_target_id_only_when_explicitly_requested()
    {
        var id = "11111111-1111-7111-8111-111111111111";
        var path = await WriteJsonlAsync("duplicate.jsonl", ValidRollout(id, "other"));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
        var service = new ConversationImportPreviewService();

        var rejected = await service.PreviewAsync([path], "openai", ids, DuplicateIdResolution.Reject);
        var copied = await service.PreviewAsync([path], "openai", ids, DuplicateIdResolution.GenerateNewId);

        Assert.Empty(rejected.Candidates);
        Assert.Contains(rejected.Issues, issue => issue.Message.Contains("已存在", StringComparison.Ordinal));
        var candidate = Assert.Single(copied.Candidates);
        Assert.NotEqual(id, candidate.TargetId);
        Assert.True(candidate.HasDuplicateId);
    }

    [Fact]
    public async Task Preview_uses_first_user_message_when_session_meta_has_no_title()
    {
        var id = "22222222-2222-7222-8222-222222222222";
        var path = await WriteJsonlAsync("untitled.jsonl", ValidRollout(id, "openai", includeTitle: false) +
            new JsonObject
            {
                ["type"] = "event_msg",
                ["payload"] = new JsonObject
                {
                    ["type"] = "user_message",
                    ["message"] = "请帮我整理这份项目的导入流程"
                }
            }.ToJsonString() + Environment.NewLine);

        var preview = await new ConversationImportPreviewService().PreviewAsync(
            [path], "openai", new HashSet<string>(), DuplicateIdResolution.Reject);

        Assert.Equal("请帮我整理这份项目的导入流程", Assert.Single(preview.Candidates).Title);
    }

    [Fact]
    public async Task Preview_uses_modern_user_message_when_session_meta_has_no_title()
    {
        var id = "33333333-3333-7333-8333-333333333333";
        var path = await WriteJsonlAsync("modern-untitled.jsonl", ValidRollout(id, "openai", includeTitle: false) +
            new JsonObject
            {
                ["type"] = "event_msg",
                ["payload"] = new JsonObject
                {
                    ["type"] = "item_completed",
                    ["item"] = new JsonObject
                    {
                        ["type"] = "UserMessage",
                        ["content"] = new JsonArray(new JsonObject { ["type"] = "Text", ["text"] = "A modern title" })
                    }
                }
            }.ToJsonString() + Environment.NewLine);

        var preview = await new ConversationImportPreviewService().PreviewAsync(
            [path], "openai", new HashSet<string>(), DuplicateIdResolution.Reject);

        Assert.Equal("A modern title", Assert.Single(preview.Candidates).Title);
    }

    private async Task<string> WriteJsonlAsync(string fileName, string content)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, fileName);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static string ValidRollout(string id, string provider = "openai", bool includeTitle = true) =>
        new JsonObject
        {
            ["type"] = "session_meta",
            ["payload"] = new JsonObject
            {
                ["id"] = id,
                ["timestamp"] = "2026-08-18T00:00:00Z",
                ["cwd"] = "D:\\imported",
                ["source"] = "cli",
                ["model_provider"] = provider,
                ["title"] = includeTitle ? "Imported conversation" : null
            }
        }.ToJsonString() + Environment.NewLine;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
