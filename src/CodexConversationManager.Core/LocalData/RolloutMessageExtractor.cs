using System.Text.Json;

namespace CodexConversationManager.Core.LocalData;

public sealed record RolloutDisplayMessage(string Role, string Kind, string Text, string? Identity);

public static class RolloutMessageExtractor
{
    public static bool TryExtract(JsonElement root, out RolloutDisplayMessage message)
    {
        message = null!;
        if (root.ValueKind != JsonValueKind.Object || !TryGetString(root, "type", out var envelopeType))
        {
            return false;
        }

        if (string.Equals(envelopeType, "response_item", StringComparison.Ordinal))
        {
            return root.TryGetProperty("payload", out var payload) && TryExtractResponseItem(payload, out message);
        }

        if (!string.Equals(envelopeType, "event_msg", StringComparison.Ordinal) ||
            !root.TryGetProperty("payload", out var eventPayload) ||
            eventPayload.ValueKind != JsonValueKind.Object ||
            !TryGetString(eventPayload, "type", out var eventType))
        {
            return false;
        }

        if (string.Equals(eventType, "item_completed", StringComparison.Ordinal))
        {
            return eventPayload.TryGetProperty("item", out var item) && TryExtractCompletedItem(item, out message);
        }

        if (!TryGetString(eventPayload, "message", out var legacyText) && !TryGetString(eventPayload, "text", out legacyText))
        {
            return false;
        }

        var role = eventType.Contains("user", StringComparison.OrdinalIgnoreCase)
            ? "user"
            : eventType.Contains("assistant", StringComparison.OrdinalIgnoreCase) ||
              eventType.Contains("agent", StringComparison.OrdinalIgnoreCase)
                ? "assistant"
                : "unknown";
        return Create(role, eventType, legacyText, identity: null, out message);
    }

    private static bool TryExtractCompletedItem(JsonElement item, out RolloutDisplayMessage message)
    {
        message = null!;
        if (item.ValueKind != JsonValueKind.Object || !TryGetString(item, "type", out var type))
        {
            return false;
        }

        var role = type switch
        {
            "UserMessage" => "user",
            "AgentMessage" => "assistant",
            _ => null
        };
        if (role is null || !TryReadContent(item, out var text))
        {
            return false;
        }

        var kind = TryGetString(item, "phase", out var phase) && !string.IsNullOrWhiteSpace(phase)
            ? phase
            : type;
        TryGetString(item, "id", out var identity);
        return Create(role, kind, text, identity, out message);
    }

    private static bool TryExtractResponseItem(JsonElement payload, out RolloutDisplayMessage message)
    {
        message = null!;
        if (payload.ValueKind != JsonValueKind.Object || !TryGetString(payload, "role", out var role) ||
            !TryReadContent(payload, out var text))
        {
            return false;
        }

        TryGetString(payload, "type", out var kind);
        TryGetString(payload, "id", out var identity);
        return Create(role, kind ?? "message", text, identity, out message);
    }

    private static bool TryReadContent(JsonElement source, out string text)
    {
        text = string.Empty;
        if (!source.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var values = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object || !TryGetString(part, "text", out var partText) ||
                string.IsNullOrWhiteSpace(partText))
            {
                continue;
            }

            values.Add(partText);
        }

        text = string.Join("\n", values);
        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool Create(string role, string kind, string text, string? identity, out RolloutDisplayMessage message)
    {
        message = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        message = new RolloutDisplayMessage(role, kind, text, identity);
        return true;
    }

    private static bool TryGetString(JsonElement source, string propertyName, out string value)
    {
        value = string.Empty;
        return source.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }
}
