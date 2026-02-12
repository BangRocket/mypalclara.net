using System.Text.Json;
using Clara.Tools.Backfill.Models;

namespace Clara.Tools.Backfill.Parsers;

/// <summary>
/// Parses Discord chat export JSON files (DiscordChatExporter format).
/// Flat array of messages, paired by user→bot response.
/// Session boundaries at 30-minute gaps.
/// </summary>
public static class DiscordParser
{
    private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(30);

    public static List<BackfillConversation> ParseDmFile(string filePath)
    {
        return ParseFile(filePath, "discord-dm", isServer: false);
    }

    public static List<BackfillConversation> ParseServerFile(string filePath)
    {
        return ParseFile(filePath, "discord-server", isServer: true);
    }

    private static List<BackfillConversation> ParseFile(string filePath, string sourceType, bool isServer)
    {
        var json = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var channelName = root.TryGetProperty("channel", out var channel) &&
                          channel.TryGetProperty("name", out var nameProp)
            ? nameProp.GetString() ?? ""
            : "";

        var channelId = root.TryGetProperty("channel", out var ch) &&
                        ch.TryGetProperty("id", out var idProp)
            ? idProp.GetString() ?? ""
            : "";

        if (!root.TryGetProperty("messages", out var messagesArray))
            return [];

        // Parse all messages
        var messages = new List<ParsedMessage>();
        foreach (var msg in messagesArray.EnumerateArray())
        {
            var parsed = ParseMessage(msg);
            if (parsed is not null)
                messages.Add(parsed);
        }

        // Split into sessions by 30-minute gaps
        var sessions = SplitSessions(messages);

        var conversations = new List<BackfillConversation>();
        var sessionIndex = 0;

        foreach (var session in sessions)
        {
            var exchanges = PairExchanges(session, isServer);
            if (exchanges.Count == 0)
                continue;

            var sourceId = $"{sourceType}-{channelId}-{sessionIndex}";
            var title = isServer
                ? $"#{channelName} session {sessionIndex}"
                : $"DM with {channelName} session {sessionIndex}";

            conversations.Add(new BackfillConversation(sourceId, sourceType, title, exchanges));
            sessionIndex++;
        }

        return conversations;
    }

    private static ParsedMessage? ParseMessage(JsonElement msg)
    {
        // Skip system messages
        if (msg.TryGetProperty("type", out var typeProp))
        {
            var type = typeProp.GetString();
            if (type is "SystemMessage" or "ThreadCreated" or "ChannelPinnedMessage" or "GuildMemberJoin")
                return null;
        }

        var content = msg.TryGetProperty("content", out var contentProp)
            ? contentProp.GetString() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(content))
            return null;

        var isBot = msg.TryGetProperty("author", out var author) &&
                    author.TryGetProperty("isBot", out var isBotProp) &&
                    isBotProp.GetBoolean();

        var authorName = author.TryGetProperty("nickname", out var nickProp) && nickProp.ValueKind == JsonValueKind.String
            ? nickProp.GetString()
            : author.TryGetProperty("name", out var authorNameProp)
                ? authorNameProp.GetString()
                : "Unknown";

        var timestamp = DateTime.UtcNow;
        if (msg.TryGetProperty("timestamp", out var tsProp) &&
            tsProp.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(tsProp.GetString(), out var dto))
                timestamp = dto.UtcDateTime;
        }

        return new ParsedMessage(content, isBot, authorName ?? "Unknown", timestamp);
    }

    /// <summary>Split messages into sessions based on time gaps.</summary>
    private static List<List<ParsedMessage>> SplitSessions(List<ParsedMessage> messages)
    {
        if (messages.Count == 0) return [];

        var sessions = new List<List<ParsedMessage>>();
        var current = new List<ParsedMessage> { messages[0] };

        for (var i = 1; i < messages.Count; i++)
        {
            if (messages[i].Timestamp - messages[i - 1].Timestamp > SessionGap)
            {
                sessions.Add(current);
                current = [];
            }
            current.Add(messages[i]);
        }

        if (current.Count > 0)
            sessions.Add(current);

        return sessions;
    }

    /// <summary>Pair user messages with following bot responses.</summary>
    private static List<BackfillExchange> PairExchanges(List<ParsedMessage> session, bool isServer)
    {
        var exchanges = new List<BackfillExchange>();

        for (var i = 0; i < session.Count - 1; i++)
        {
            var msg = session[i];
            if (msg.IsBot)
                continue; // skip bot messages as initiators

            // Find next bot response
            for (var j = i + 1; j < session.Count; j++)
            {
                if (session[j].IsBot)
                {
                    exchanges.Add(new BackfillExchange(
                        UserMessage: msg.Content,
                        AssistantMessage: session[j].Content,
                        UserTimestamp: msg.Timestamp,
                        AssistantTimestamp: session[j].Timestamp,
                        UserDisplayName: isServer ? msg.AuthorName : null));
                    break;
                }
            }
        }

        return exchanges;
    }

    private sealed record ParsedMessage(
        string Content,
        bool IsBot,
        string AuthorName,
        DateTime Timestamp);
}
