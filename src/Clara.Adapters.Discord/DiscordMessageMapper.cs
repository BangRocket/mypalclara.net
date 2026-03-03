using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace Clara.Adapters.Discord;

/// <summary>
/// Maps Discord messages to gateway-compatible format.
/// </summary>
public static class DiscordMessageMapper
{
    /// <summary>
    /// Build a session key from Discord context.
    /// DM: clara:main:discord:dm:{userId}
    /// Channel: clara:main:discord:channel:{channelId}
    /// </summary>
    public static string BuildSessionKey(MessageCreateEventArgs e)
    {
        if (e.Channel.IsPrivate)
            return $"clara:main:discord:dm:{e.Author.Id}";
        return $"clara:main:discord:channel:{e.Channel.Id}";
    }

    /// <summary>
    /// Extract the text content from a Discord message, cleaning bot mentions.
    /// </summary>
    public static string ExtractContent(DiscordMessage message, ulong botId)
    {
        var content = message.Content ?? "";

        // Remove bot mention from the start
        content = content.Replace($"<@{botId}>", "").Replace($"<@!{botId}>", "").Trim();

        return content;
    }

    /// <summary>
    /// Get image attachment URLs from a message.
    /// </summary>
    public static IReadOnlyList<string> GetImageUrls(DiscordMessage message, int maxImages = 1)
    {
        return message.Attachments
            .Where(a => a.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            .Take(maxImages)
            .Select(a => a.Url)
            .ToList();
    }

    /// <summary>
    /// Extract the user display name.
    /// </summary>
    public static string GetDisplayName(DiscordUser user)
    {
        return user.Username;
    }
}
