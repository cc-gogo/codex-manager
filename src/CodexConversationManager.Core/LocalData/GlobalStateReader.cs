using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexConversationManager.Core.LocalData;

public sealed class GlobalStateReader(string path) : IGlobalStateEvidenceSource
{
    public async Task<IReadOnlyList<GlobalStateReference>> ReadReferencesAsync(
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ReadOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < 3 && exception is JsonException or IOException)
            {
                // Codex can expose a partially written state file for a brief moment.
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<IReadOnlyList<GlobalStateReference>> ReadOnceAsync(CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var references = new List<GlobalStateReference>();
        Visit(root, "$", references);
        return references;
    }

    private static void Visit(JsonNode? node, string currentPath, List<GlobalStateReference> references)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject)
                {
                    var propertyPath = AppendProperty(currentPath, property.Key);
                    if (IsUuid(property.Key))
                    {
                        references.Add(new GlobalStateReference(property.Key.ToLowerInvariant(), propertyPath));
                    }

                    Visit(property.Value, propertyPath, references);
                }

                break;

            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    Visit(jsonArray[index], $"{currentPath}[{index}]", references);
                }

                break;

            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var value) && IsUuid(value):
                references.Add(new GlobalStateReference(value.ToLowerInvariant(), currentPath));
                break;
        }
    }

    private static string AppendProperty(string path, string property) =>
        IsSimpleName(property) ? $"{path}.{property}" : $"{path}['{property.Replace("'", "\\'", StringComparison.Ordinal)}']";

    private static bool IsSimpleName(string value) =>
        value.Length > 0 &&
        (char.IsLetter(value[0]) || value[0] == '_') &&
        value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static bool IsUuid(string? value) => Guid.TryParseExact(value, "D", out _);
}
