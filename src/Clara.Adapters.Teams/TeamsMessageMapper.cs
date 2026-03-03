using System.Text.Json;

namespace Clara.Adapters.Teams;

/// <summary>
/// Maps Teams Bot Framework activities to gateway-compatible format.
/// </summary>
public static class TeamsMessageMapper
{
    /// <summary>
    /// Build a session key from Teams conversation context.
    /// Personal (1:1): clara:main:teams:chat:{conversationId}
    /// Channel:        clara:main:teams:channel:{channelId}
    /// </summary>
    public static string BuildSessionKey(JsonElement activity)
    {
        var conversationType = activity.TryGetProperty("conversation", out var conv)
            && conv.TryGetProperty("conversationType", out var ct)
                ? ct.GetString()
                : null;

        var conversationId = conv.TryGetProperty("id", out var cid)
            ? cid.GetString() ?? "unknown"
            : "unknown";

        if (conversationType == "channel" || conversationType == "groupChat")
        {
            // Use channel ID from channelData if available, otherwise conversation ID
            var channelId = activity.TryGetProperty("channelData", out var cd)
                && cd.TryGetProperty("channel", out var ch)
                && ch.TryGetProperty("id", out var chId)
                    ? chId.GetString() ?? conversationId
                    : conversationId;
            return $"clara:main:teams:channel:{channelId}";
        }

        return $"clara:main:teams:chat:{conversationId}";
    }

    /// <summary>
    /// Extract the text content from a Teams activity, stripping bot mentions.
    /// </summary>
    public static string ExtractContent(JsonElement activity, string? botName = null)
    {
        var text = activity.TryGetProperty("text", out var t)
            ? t.GetString() ?? ""
            : "";

        // Remove bot mentions: <at>BotName</at>
        if (!string.IsNullOrEmpty(botName))
        {
            text = text.Replace($"<at>{botName}</at>", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        // Also strip generic at-mention XML tags
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<at>.*?</at>", "").Trim();

        return text;
    }

    /// <summary>
    /// Extract the sender's display name from a Teams activity.
    /// </summary>
    public static string GetDisplayName(JsonElement activity)
    {
        if (activity.TryGetProperty("from", out var from) && from.TryGetProperty("name", out var name))
            return name.GetString() ?? "Unknown";
        return "Unknown";
    }

    /// <summary>
    /// Extract the sender's user ID (AAD object ID) from a Teams activity.
    /// </summary>
    public static string GetUserId(JsonElement activity)
    {
        if (activity.TryGetProperty("from", out var from))
        {
            // Prefer aadObjectId for Azure AD users
            if (from.TryGetProperty("aadObjectId", out var aad))
                return aad.GetString() ?? "unknown";
            if (from.TryGetProperty("id", out var id))
                return id.GetString() ?? "unknown";
        }
        return "unknown";
    }

    /// <summary>
    /// Extract the service URL from a Teams activity (needed for sending replies).
    /// </summary>
    public static string GetServiceUrl(JsonElement activity)
    {
        return activity.TryGetProperty("serviceUrl", out var su)
            ? su.GetString() ?? ""
            : "";
    }

    /// <summary>
    /// Extract the conversation ID from a Teams activity.
    /// </summary>
    public static string GetConversationId(JsonElement activity)
    {
        return activity.TryGetProperty("conversation", out var conv)
            && conv.TryGetProperty("id", out var id)
                ? id.GetString() ?? ""
                : "";
    }

    /// <summary>
    /// Extract image attachment URLs from a Teams activity.
    /// </summary>
    public static IReadOnlyList<string> GetImageUrls(JsonElement activity)
    {
        var urls = new List<string>();
        if (activity.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var att in attachments.EnumerateArray())
            {
                var contentType = att.TryGetProperty("contentType", out var ct) ? ct.GetString() : null;
                if (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (att.TryGetProperty("contentUrl", out var url))
                        urls.Add(url.GetString() ?? "");
                }
            }
        }
        return urls;
    }
}
