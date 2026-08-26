using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexConversationManager.Core.Deletion;

public sealed class GlobalStateIdRemover
{
    public async Task RemoveAsync(
        string path,
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return;
        }

        JsonNode? root;
        await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            root = await JsonNode.ParseAsync(input, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (root is null || !Remove(root, id))
        {
            return;
        }

        var temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var output = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(output, root, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var validation = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                _ = await JsonNode.ParseAsync(validation, cancellationToken: cancellationToken).ConfigureAwait(false)
                    ?? throw new JsonException("Updated global state is empty.");
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool Remove(JsonNode node, string id)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject value:
                foreach (var property in value.ToList())
                {
                    if (string.Equals(property.Key, id, StringComparison.OrdinalIgnoreCase))
                    {
                        value.Remove(property.Key);
                        changed = true;
                    }
                    else if (property.Value is not null)
                    {
                        changed |= Remove(property.Value, id);
                    }
                }

                break;
            case JsonArray value:
                for (var index = value.Count - 1; index >= 0; index--)
                {
                    if (value[index] is JsonValue item && item.TryGetValue<string>(out var text) &&
                        string.Equals(text, id, StringComparison.OrdinalIgnoreCase))
                    {
                        value.RemoveAt(index);
                        changed = true;
                    }
                    else if (value[index] is { } child)
                    {
                        changed |= Remove(child, id);
                    }
                }

                break;
        }

        return changed;
    }
}
