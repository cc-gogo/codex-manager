using System.Text;
using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.Inventory;

namespace CodexConversationManager.Core.Export;

public sealed class ConversationMarkdownExporter
{
    public async Task ExportAsync(ConversationRecord record, ConversationDetail detail, string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var content = new StringBuilder()
            .AppendLine($"# {record.DisplayTitle}")
            .AppendLine()
            .AppendLine($"- Task ID: `{record.Id}`")
            .AppendLine($"- Source: {detail.Source}")
            .AppendLine($"- Exported: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}")
            .AppendLine();
        foreach (var block in detail.Blocks)
        {
            content.AppendLine($"## {block.Role}").AppendLine().AppendLine(block.Text).AppendLine();
        }
        await File.WriteAllTextAsync(outputPath, content.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }
}
