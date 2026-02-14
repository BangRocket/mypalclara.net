using System.Text.Json;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Adapters.WhatsApp;

/// <summary>Handles WhatsApp Cloud API webhook verification and incoming messages.</summary>
public sealed class WhatsAppWebhookHandler
{
    private readonly GatewayClient _gateway;
    private readonly WhatsAppMessageSender _sender;
    private readonly ClaraConfig _config;
    private readonly ILogger<WhatsAppWebhookHandler> _logger;

    public WhatsAppWebhookHandler(
        GatewayClient gateway,
        WhatsAppMessageSender sender,
        ClaraConfig config,
        ILogger<WhatsAppWebhookHandler> logger)
    {
        _gateway = gateway;
        _sender = sender;
        _config = config;
        _logger = logger;
    }

    /// <summary>Handle GET webhook verification from WhatsApp.</summary>
    public IResult HandleVerification(HttpContext ctx)
    {
        var mode = ctx.Request.Query["hub.mode"].FirstOrDefault();
        var token = ctx.Request.Query["hub.verify_token"].FirstOrDefault();
        var challenge = ctx.Request.Query["hub.challenge"].FirstOrDefault();

        if (mode == "subscribe" && token == _config.WhatsApp.VerifyToken)
        {
            _logger.LogInformation("Webhook verified successfully");
            return Results.Ok(challenge);
        }

        _logger.LogWarning("Webhook verification failed: invalid token");
        return Results.Forbid();
    }

    /// <summary>Handle POST incoming messages from WhatsApp.</summary>
    public async Task<IResult> HandleIncomingAsync(HttpContext ctx)
    {
        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        var root = doc.RootElement;

        // WhatsApp Cloud API sends notifications under entry[].changes[].value.messages[]
        if (!root.TryGetProperty("entry", out var entries))
            return Results.Ok(); // Not a message notification

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes))
                continue;

            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value))
                    continue;

                if (!value.TryGetProperty("messages", out var messages))
                    continue;

                // Get contact info if available
                var contacts = value.TryGetProperty("contacts", out var c) ? c : default;

                foreach (var message in messages.EnumerateArray())
                {
                    await ProcessMessageAsync(message, contacts);
                }
            }
        }

        return Results.Ok();
    }

    private async Task ProcessMessageAsync(JsonElement message, JsonElement contacts)
    {
        var messageType = message.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (messageType != "text") return; // Only handle text messages for now

        var from = message.GetProperty("from").GetString()!;
        var text = message.GetProperty("text").GetProperty("body").GetString()!;

        // Try to get contact name
        var displayName = from;
        if (contacts.ValueKind == JsonValueKind.Array)
        {
            foreach (var contact in contacts.EnumerateArray())
            {
                if (contact.TryGetProperty("wa_id", out var waId) && waId.GetString() == from)
                {
                    if (contact.TryGetProperty("profile", out var profile) &&
                        profile.TryGetProperty("name", out var name))
                    {
                        displayName = name.GetString() ?? from;
                    }
                    break;
                }
            }
        }

        _logger.LogInformation("Message from {User}: {Text}",
            displayName, text[..Math.Min(text.Length, 50)]);

        var request = new ChatRequest(
            ChannelId: from,
            ChannelName: displayName,
            ChannelType: "dm",
            UserId: from,
            DisplayName: displayName,
            Content: text);

        var responseText = "";

        await foreach (var response in _gateway.ChatAsync(request))
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

        if (!string.IsNullOrEmpty(responseText))
        {
            await _sender.SendTextMessageAsync(from, responseText);
        }
    }
}
