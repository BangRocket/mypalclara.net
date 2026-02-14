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

    /// <summary>Maximum total token budget for history (estimated).</summary>
    public int MaxHistoryTokens { get; set; } = 20_000;

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

    /// <summary>Trim history to fit within message count and token budget.</summary>
    private void TrimHistory()
    {
        // Trim by count
        while (History.Count > MaxHistoryMessages)
            History.RemoveAt(0);

        // Trim by token budget
        var totalTokens = TokenEstimator.EstimateMessages(History);
        while (totalTokens > MaxHistoryTokens && History.Count > 2)
        {
            totalTokens -= TokenEstimator.Estimate(History[0].Content) + 4;
            History.RemoveAt(0);
        }
    }
}
