using System.Collections.Concurrent;
using System.Text;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;

namespace Clara.Adapters.Discord;

/// <summary>
/// Manages sending streamed responses back to Discord channels.
/// Handles batched updates to avoid rate limits.
/// </summary>
public class DiscordResponseSender
{
    private readonly DiscordClient _client;
    private readonly ILogger<DiscordResponseSender> _logger;
    private readonly ConcurrentDictionary<string, ResponseState> _activeResponses = new();

    private const int UpdateIntervalMs = 500;
    private const int MaxMessageLength = 2000;

    public DiscordResponseSender(DiscordClient client, ILogger<DiscordResponseSender> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Register a channel for a session key so we know where to send responses.
    /// </summary>
    public void RegisterChannel(string sessionKey, DiscordChannel channel)
    {
        _activeResponses[sessionKey] = new ResponseState(channel);
    }

    /// <summary>
    /// Append text delta to the response buffer.
    /// Periodically flushes to Discord to show streaming.
    /// </summary>
    public async Task AppendTextAsync(string sessionKey, string text)
    {
        if (!_activeResponses.TryGetValue(sessionKey, out var state))
            return;

        state.Buffer.Append(text);

        // Only update Discord at intervals to avoid rate limits
        if ((DateTime.UtcNow - state.LastUpdate).TotalMilliseconds >= UpdateIntervalMs)
        {
            await FlushAsync(sessionKey, state);
        }
    }

    /// <summary>
    /// Handle tool status updates -- show as typing indicator.
    /// </summary>
    public async Task ShowToolStatusAsync(string sessionKey, string toolName, string status)
    {
        if (!_activeResponses.TryGetValue(sessionKey, out var state))
            return;

        try
        {
            await state.Channel.TriggerTypingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to trigger typing for {SessionKey}", sessionKey);
        }
    }

    /// <summary>
    /// Mark response as complete -- flush remaining buffer.
    /// </summary>
    public async Task CompleteAsync(string sessionKey)
    {
        if (!_activeResponses.TryRemove(sessionKey, out var state))
            return;

        if (state.Buffer.Length > 0)
            await FlushAsync(sessionKey, state, final: true);
    }

    /// <summary>
    /// Send an error message to the channel.
    /// </summary>
    public async Task SendErrorAsync(string sessionKey, string error)
    {
        if (!_activeResponses.TryRemove(sessionKey, out var state))
            return;

        try
        {
            await state.Channel.SendMessageAsync($"An error occurred: {error}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send error message for {SessionKey}", sessionKey);
        }
    }

    private async Task FlushAsync(string sessionKey, ResponseState state, bool final = false)
    {
        var content = state.Buffer.ToString();
        if (string.IsNullOrEmpty(content)) return;

        // Truncate if too long for Discord
        if (content.Length > MaxMessageLength)
            content = content[..MaxMessageLength];

        try
        {
            if (state.SentMessage is null)
            {
                // Send initial message
                state.SentMessage = await state.Channel.SendMessageAsync(content);
            }
            else
            {
                // Edit existing message with updated content
                await state.SentMessage.ModifyAsync(content);
            }

            state.LastUpdate = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush response for {SessionKey}", sessionKey);
        }
    }

    private class ResponseState
    {
        public DiscordChannel Channel { get; }
        public StringBuilder Buffer { get; } = new();
        public DiscordMessage? SentMessage { get; set; }
        public DateTime LastUpdate { get; set; } = DateTime.MinValue;

        public ResponseState(DiscordChannel channel) => Channel = channel;
    }
}
