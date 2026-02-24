using System.Text.Json;

namespace MyPalClara.Core.Protocol;

/// <summary>
/// Parses incoming WebSocket messages by type field and serializes outgoing messages.
/// </summary>
public static class MessageParser
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Parse an incoming JSON message, extracting the "type" field and raw data.
    /// </summary>
    /// <param name="json">Raw JSON string from WebSocket.</param>
    /// <returns>Tuple of (message type string, parsed JsonElement for further deserialization).</returns>
    /// <exception cref="JsonException">If JSON is malformed.</exception>
    /// <exception cref="InvalidOperationException">If "type" field is missing.</exception>
    public static (string Type, JsonElement Data) Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Message missing 'type' field");
        }

        var type = typeProp.GetString()
            ?? throw new InvalidOperationException("Message 'type' field is null");

        // Clone the root element so it survives the JsonDocument disposal
        return (type, root.Clone());
    }

    /// <summary>
    /// Deserialize a JsonElement to a specific message type.
    /// </summary>
    public static T Deserialize<T>(JsonElement element)
    {
        return JsonSerializer.Deserialize<T>(element.GetRawText(), SerializeOptions)
            ?? throw new JsonException($"Failed to deserialize message as {typeof(T).Name}");
    }

    /// <summary>
    /// Serialize a protocol message to JSON string.
    /// </summary>
    public static string Serialize<T>(T message)
    {
        return JsonSerializer.Serialize(message, SerializeOptions);
    }
}
