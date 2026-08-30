using System.Text.Json;
using System.Text.Json.Nodes;
using CodexConversationManager.Core.AppServer;
using CodexConversationManager.Core.Domain;
using CodexConversationManager.Core.LocalData;

namespace CodexConversationManager.Core.Inventory;

public enum ConversationDetailSource
{
    AppServer,
    SessionFile
}

public sealed record ConversationDetailBlock(string Role, string Kind, string Text);

public sealed record ConversationDetail(
    string Id,
    ConversationDetailSource Source,
    IReadOnlyList<ConversationDetailBlock> Blocks);

public interface IConversationDetailProvider
{
    Task<ConversationDetail> LoadAsync(
        ConversationRecord record,
        CancellationToken cancellationToken = default);
}

public sealed class ConversationDetailService(IConversationDetailReader? appServer, bool preferLocalSession = true) : IConversationDetailProvider
{
    private const int MaximumFallbackRecords = 512;
    private const int MaximumBlockLength = 32 * 1024;

    public async Task<ConversationDetail> LoadAsync(
        ConversationRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var localPaths = record.Evidence.ActiveSessionPaths.Concat(record.Evidence.ArchivedSessionPaths).Where(File.Exists).ToList();
        if (preferLocalSession && localPaths.Count > 0)
        {
            var localBlocks = await ReadSessionFallbackAsync(record.Evidence, cancellationToken).ConfigureAwait(false);
            if (localBlocks.Count > 0)
            {
                return new ConversationDetail(record.Id, ConversationDetailSource.SessionFile, localBlocks);
            }
        }

        if (appServer is null)
        {
            return new ConversationDetail(record.Id, ConversationDetailSource.SessionFile, []);
        }

        try
        {
            var thread = await appServer.ReadThreadAsync(record.Id, includeTurns: true, cancellationToken)
                .ConfigureAwait(false);
            return new ConversationDetail(record.Id, ConversationDetailSource.AppServer, ExtractBlocks(thread.Raw));
        }
        catch when (localPaths.Count > 0)
        {
            var blocks = await ReadSessionFallbackAsync(record.Evidence, cancellationToken).ConfigureAwait(false);
            return new ConversationDetail(record.Id, ConversationDetailSource.SessionFile, blocks);
        }
    }

    private static IReadOnlyList<ConversationDetailBlock> ExtractBlocks(JsonNode node)
    {
        var blocks = new List<ConversationDetailBlock>();
        Visit(node, "unknown", blocks);
        return blocks;
    }

    private static void Visit(JsonNode? node, string role, List<ConversationDetailBlock> blocks)
    {
        switch (node)
        {
            case JsonObject value:
            {
                var objectRole = ReadString(value["role"]) ?? role;
                var kind = ReadString(value["type"]) ?? "text";
                var text = ReadString(value["text"]) ?? ReadString(value["message"]);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    blocks.Add(new ConversationDetailBlock(objectRole, kind, Limit(text)));
                    return;
                }

                foreach (var property in value)
                {
                    if (property.Key is "role" or "type")
                    {
                        continue;
                    }

                    Visit(property.Value, objectRole, blocks);
                }

                break;
            }
            case JsonArray value:
                foreach (var item in value)
                {
                    Visit(item, role, blocks);
                }

                break;
        }
    }

    private static async Task<IReadOnlyList<ConversationDetailBlock>> ReadSessionFallbackAsync(
        ConversationEvidence evidence,
        CancellationToken cancellationToken)
    {
        var blocks = new List<ConversationDetailBlock>();
        foreach (var path in evidence.ActiveSessionPaths.Concat(evidence.ArchivedSessionPaths))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
            for (var index = 0; index < MaximumFallbackRecords; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!RolloutMessageExtractor.TryExtract(root, out var message))
                    {
                        continue;
                    }

                    if (IsLegacyCompatibilityEvent(root) && blocks.Count > 0 &&
                        string.Equals(blocks[^1].Role, message.Role, StringComparison.Ordinal) &&
                        string.Equals(blocks[^1].Text, message.Text, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    blocks.Add(new ConversationDetailBlock(message.Role, message.Kind, Limit(message.Text)));
                }
                catch (JsonException)
                {
                    // A damaged record remains visible in the inventory; skip only its unreadable detail line.
                }
            }

            if (blocks.Count > 0)
            {
                return blocks;
            }
        }

        return blocks;
    }

    private static bool IsLegacyCompatibilityEvent(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty("type", out var envelopeType) &&
               string.Equals(envelopeType.GetString(), "event_msg", StringComparison.Ordinal) &&
               root.TryGetProperty("payload", out var payload) &&
               payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty("type", out var type) &&
               (string.Equals(type.GetString(), "user_message", StringComparison.Ordinal) ||
                string.Equals(type.GetString(), "agent_message", StringComparison.Ordinal));
    }

    private static string? ReadString(JsonNode? value) =>
        value is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static string Limit(string text) =>
        text.Length <= MaximumBlockLength ? text : text[..MaximumBlockLength];
}
