using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.Export;
using CodexConversationManager.Core.Inventory;
using Xunit;

namespace CodexConversationManager.Tests.Export;

public sealed class ConversationMarkdownExporterTests
{
    [Fact]
    public async Task Export_writes_title_id_and_structured_messages()
    {
        var path = Path.Combine(Path.GetTempPath(), $"conversation-{Guid.NewGuid():N}.md");
        var record = new ConversationRecord("thread-1", "A title", ConversationCategory.Normal, "cli", "D:\\work", null, null, true, ConversationEvidence.Empty("thread-1"));
        var detail = new ConversationDetail("thread-1", ConversationDetailSource.SessionFile,
            [new ConversationDetailBlock("user", "message", "Hello"), new ConversationDetailBlock("assistant", "message", "World")]);

        await new ConversationMarkdownExporter().ExportAsync(record, detail, path);

        var markdown = await File.ReadAllTextAsync(path);
        Assert.Contains("# A title", markdown);
        Assert.Contains("`thread-1`", markdown);
        Assert.Contains("## user", markdown);
        Assert.Contains("World", markdown);
        File.Delete(path);
    }
}
