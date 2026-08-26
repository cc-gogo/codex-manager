using System.Text.Json;

namespace CodexConversationManager.Core.LocalData;

public sealed class SessionIndexReader(string path) : ISessionIndexEvidenceSource
{
    public async Task<IReadOnlyList<SessionIndexEvidence>> ReadEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return [];

        var entries = new List<SessionIndexEvidence>();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var idProperty) || !Guid.TryParseExact(idProperty.GetString(), "D", out var id)) continue;
                var title = root.TryGetProperty("thread_name", out var titleProperty) ? titleProperty.GetString() : null;
                DateTimeOffset? updated = root.TryGetProperty("updated_at", out var updatedProperty) &&
                                          DateTimeOffset.TryParse(updatedProperty.GetString(), out var value) ? value : null;
                entries.Add(new SessionIndexEvidence(id.ToString("D"), title ?? id.ToString("D"), updated, path));
            }
            catch (JsonException)
            {
            }
        }

        return entries;
    }
}
