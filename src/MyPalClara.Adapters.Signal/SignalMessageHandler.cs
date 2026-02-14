using System.Text.Json;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Adapters.Signal;

/// <summary>
/// Parses signal-cli JSON-RPC messages and routes them through the Gateway.
/// </summary>
public sealed class SignalMessageHandler
{
    private readonly GatewayClient _gateway;
    private readonly SignalBotService _signal;
    private readonly ClaraConfig _config;
    private readonly ILogger<SignalMessageHandler> _logger;

    public SignalMessageHandler(
        GatewayClient gateway,
        SignalBotService signal,
        ClaraConfig config,
        ILogger<SignalMessageHandler> logger)
    {
        _gateway = gateway;
        _signal = signal;
        _config = config;
        _logger = logger;
    }

    public async Task HandleJsonRpcMessageAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // signal-cli JSON-RPC notifications have a "method" field
        if (!root.TryGetProperty("method", out var methodProp))
            return;

        if (methodProp.GetString() != "receive")
            return;

        if (!root.TryGetProperty("params", out var paramsEl))
            return;

        // Extract envelope
        if (!paramsEl.TryGetProperty("envelope", out var envelope))
            return;

        if (!envelope.TryGetProperty("dataMessage", out var dataMsg))
            return;

        var message = dataMsg.TryGetProperty("message", out var msgProp)
            ? msgProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(message))
            return;

        var sourceNumber = envelope.TryGetProperty("sourceNumber", out var srcProp)
            ? srcProp.GetString() ?? ""
            : "";

        var sourceName = envelope.TryGetProperty("sourceName", out var nameProp)
            ? nameProp.GetString() ?? sourceNumber
            : sourceNumber;

        // Check allowed numbers
        if (_config.Signal.AllowedNumbers.Count > 0 &&
            !_config.Signal.AllowedNumbers.Contains(sourceNumber))
        {
            _logger.LogDebug("Ignoring message from unauthorized number {Number}", sourceNumber);
            return;
        }

        _logger.LogInformation("Message from {Name} ({Number}): {Text}",
            sourceName, sourceNumber, message[..Math.Min(message.Length, 50)]);

        var request = new ChatRequest(
            ChannelId: sourceNumber,
            ChannelName: sourceName,
            ChannelType: "dm",
            UserId: sourceNumber,
            DisplayName: sourceName,
            Content: message);

        var responseText = "";

        await foreach (var response in _gateway.ChatAsync(request, ct))
        {
            switch (response)
            {
                case TextChunk chunk:
                    responseText += chunk.Text;
                    break;

                case Complete complete:
                    responseText = complete.FullText;
                    break;

                case ErrorMessage error:
                    responseText = $"Error: {error.Message}";
                    break;
            }
        }

        if (string.IsNullOrEmpty(responseText)) return;

        // Split long messages
        var maxLen = _config.Signal.MaxMessageLength;
        var chunks = SplitMessage(responseText, maxLen);

        foreach (var chunk in chunks)
        {
            await _signal.SendJsonRpcAsync("send", new
            {
                recipient = new[] { sourceNumber },
                message = chunk,
            }, ct);
        }
    }

    private static List<string> SplitMessage(string text, int maxLen)
    {
        if (text.Length <= maxLen)
            return [text];

        var parts = new List<string>();
        var remaining = text.AsSpan();

        while (remaining.Length > 0)
        {
            if (remaining.Length <= maxLen)
            {
                parts.Add(remaining.ToString());
                break;
            }

            var splitAt = remaining[..maxLen].LastIndexOf('\n');
            if (splitAt < maxLen / 2)
                splitAt = remaining[..maxLen].LastIndexOf(' ');
            if (splitAt < maxLen / 4)
                splitAt = maxLen;

            parts.Add(remaining[..splitAt].ToString());
            remaining = remaining[splitAt..].TrimStart("\n ");
        }

        return parts;
    }
}
