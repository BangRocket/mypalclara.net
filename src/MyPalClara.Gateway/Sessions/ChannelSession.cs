using MyPalClara.Core.Llm;

namespace MyPalClara.Gateway.Sessions;

/// <summary>
/// Per-channel conversation state within an adapter session.
/// Tracks in-memory message history for the LLM context window.
/// </summary>
public sealed class ChannelSession
{
    public string ChannelId { get; }
    public string ChannelName { get; }
    public string ChannelType { get; }

    /// <summary>DB conversation ID for persistence.</summary>
    public Guid? ConversationId { get; set; }

    /// <summary>DB user GUID for this channel's primary user.</summary>
    public Guid? UserGuid { get; set; }

    /// <summary>In-memory message history for LLM context.</summary>
    public List<ChatMessage> History { get; } = [];

    /// <summary>Maximum number of messages to keep in memory.</summary>
    public int MaxHistoryMessages { get; set; } = 50;

    /// <summary>Maximum total character budget for history.</summary>
    public int MaxHistoryChars { get; set; } = 80_000;

    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    public ChannelSession(string channelId, string channelName, string channelType)
    {
        ChannelId = channelId;
        ChannelName = channelName;
        ChannelType = channelType;
    }

    /// <summary>Add messages and trim to budget.</summary>
    public void AddMessages(params ChatMessage[] messages)
    {
        History.AddRange(messages);
        LastActivity = DateTime.UtcNow;
        TrimHistory();
    }

    /// <summary>Trim history to fit within message count and character budget.</summary>
    private void TrimHistory()
    {
        // Trim by count
        while (History.Count > MaxHistoryMessages)
            History.RemoveAt(0);

        // Trim by character budget
        var totalChars = History.Sum(m => m.Content?.Length ?? 0);
        while (totalChars > MaxHistoryChars && History.Count > 2)
        {
            totalChars -= History[0].Content?.Length ?? 0;
            History.RemoveAt(0);
        }
    }
}
