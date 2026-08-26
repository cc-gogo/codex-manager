using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexConversationManager.Core.LocalData;

public sealed partial class SessionScanner(CodexPaths paths) : ISessionEvidenceSource
{
    private const int MaximumMetadataRecords = 32;

    public async Task<IReadOnlyList<SessionEvidence>> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<SessionEvidence>();
        await ScanDirectoryAsync(paths.Sessions, false, results, cancellationToken).ConfigureAwait(false);
        await ScanDirectoryAsync(paths.ArchivedSessions, true, results, cancellationToken).ConfigureAwait(false);
        return results;
    }

    private static async Task ScanDirectoryAsync(
        string directory,
        bool isArchived,
        List<SessionEvidence> results,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ReadEvidenceAsync(path, isArchived, cancellationToken).ConfigureAwait(false));
        }
    }

    private static async Task<SessionEvidence> ReadEvidenceAsync(
        string path,
        bool isArchived,
        CancellationToken cancellationToken)
    {
        var inferredId = InferId(path) ?? string.Empty;
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);

            for (var index = 0; index < MaximumMetadataRecords; index++)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("type", out var type) ||
                        type.GetString() != "session_meta" ||
                        !root.TryGetProperty("payload", out var payload))
                    {
                        continue;
                    }

                    var id = GetString(payload, "id") ?? inferredId;
                    if (!IsUuid(id))
                    {
                        return Error(inferredId, path, isArchived, "Session metadata contains an invalid UUID.");
                    }

                    return new SessionEvidence(
                        id,
                        path,
                        isArchived,
                        GetString(payload, "source"),
                        GetString(payload, "thread_source"),
                        GetString(payload, "cwd"),
                        ParseTimestamp(GetString(payload, "timestamp")),
                        null);
                }
                catch (JsonException exception)
                {
                    return Error(inferredId, path, isArchived, $"Invalid JSON metadata: {exception.Message}");
                }
            }

            return Error(inferredId, path, isArchived, "Session metadata was not found in the bounded header scan.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Error(inferredId, path, isArchived, $"Unable to read session metadata: {exception.Message}");
        }
    }

    private static SessionEvidence Error(string id, string path, bool isArchived, string error) =>
        new(id, path, isArchived, null, null, null, null, error);

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, out var timestamp) ? timestamp : null;

    private static string? InferId(string path)
    {
        var match = UuidPattern().Match(Path.GetFileNameWithoutExtension(path));
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    private static bool IsUuid(string? value) => Guid.TryParseExact(value, "D", out _);

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex UuidPattern();
}
