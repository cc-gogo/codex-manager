using System.Text;
using System.Text.Json.Nodes;

namespace CodexConversationManager.Core.Import;

public sealed class ConversationImportPreviewService : IConversationImportPreviewService
{
    public async Task<ConversationImportPreview> PreviewAsync(
        IReadOnlyList<string> sourcePaths,
        string currentProvider,
        IReadOnlySet<string> existingIds,
        DuplicateIdResolution duplicateResolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentProvider);
        ArgumentNullException.ThrowIfNull(existingIds);

        var candidates = new List<ConversationImportCandidate>();
        var issues = new List<ConversationImportIssue>();
        var targetIds = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        foreach (var sourcePath in sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullPath))
            {
                issues.Add(new ConversationImportIssue(fullPath, "找不到导入文件。"));
                continue;
            }

            try
            {
                var metadata = await ReadMetadataAsync(fullPath, cancellationToken).ConfigureAwait(false);
                var hasDuplicateId = targetIds.Contains(metadata.Id);
                if (hasDuplicateId && duplicateResolution == DuplicateIdResolution.Reject)
                {
                    issues.Add(new ConversationImportIssue(fullPath, $"对话 ID 已存在：{metadata.Id}"));
                    continue;
                }

                var targetId = hasDuplicateId ? Guid.NewGuid().ToString("D") : metadata.Id;
                while (!targetIds.Add(targetId)) targetId = Guid.NewGuid().ToString("D");
                candidates.Add(new ConversationImportCandidate(
                    fullPath,
                    metadata.Id,
                    targetId,
                    metadata.Title,
                    metadata.Cwd,
                    metadata.CreatedAt,
                    metadata.UpdatedAt,
                    metadata.Provider,
                    currentProvider,
                    hasDuplicateId));
            }
            catch (ConversationImportFormatException exception)
            {
                issues.Add(new ConversationImportIssue(fullPath, exception.Message));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                issues.Add(new ConversationImportIssue(fullPath, $"无法读取导入文件：{exception.Message}"));
            }
        }

        return new ConversationImportPreview(candidates, issues);
    }

    private static async Task<SessionMetadata> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        JsonObject? metadata = null;
        string? firstUserMessage = null;
        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException)
            {
                throw new ConversationImportFormatException($"第 {lineNumber} 行不是有效 JSON。", exception);
            }

            if (node is not JsonObject value)
                throw new ConversationImportFormatException($"第 {lineNumber} 行不是 JSON 对象。");
            if (string.Equals(StringValue(value["type"]), "session_meta", StringComparison.Ordinal))
            {
                metadata = value["payload"] as JsonObject
                    ?? throw new ConversationImportFormatException("session_meta 缺少 payload 对象。");
            }
            else if (firstUserMessage is null && string.Equals(StringValue(value["type"]), "event_msg", StringComparison.Ordinal) &&
                     value["payload"] is JsonObject eventPayload &&
                     string.Equals(StringValue(eventPayload["type"]), "user_message", StringComparison.Ordinal))
            {
                firstUserMessage = StringValue(eventPayload["message"]) ?? StringValue(eventPayload["text"]);
            }
        }

        if (metadata is null) throw new ConversationImportFormatException("未找到 session_meta 记录。");
        var id = StringValue(metadata["id"]);
        if (!Guid.TryParseExact(id, "D", out var parsedId))
            throw new ConversationImportFormatException("session_meta 中缺少有效 UUID 对话 ID。");

        var timestamp = ParseTimestamp(StringValue(metadata["timestamp"]));
        var title = StringValue(metadata["title"]);
        if (string.IsNullOrWhiteSpace(title)) title = NormalizeTitle(firstUserMessage) ?? parsedId.ToString("D");
        return new SessionMetadata(
            parsedId.ToString("D"),
            title,
            StringValue(metadata["cwd"]) ?? string.Empty,
            timestamp,
            timestamp,
            StringValue(metadata["model_provider"]) ?? string.Empty);
    }

    private static DateTimeOffset ParseTimestamp(string? timestamp) =>
        DateTimeOffset.TryParse(timestamp, out var parsed) ? parsed : DateTimeOffset.UtcNow;

    private static string? StringValue(JsonNode? value) =>
        value is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static string? NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var title = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return title.Length <= 120 ? title : title[..120].TrimEnd() + "...";
    }

    private sealed record SessionMetadata(
        string Id, string Title, string Cwd, DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt, string Provider);

    private sealed class ConversationImportFormatException(string message, Exception? innerException = null)
        : Exception(message, innerException);
}
