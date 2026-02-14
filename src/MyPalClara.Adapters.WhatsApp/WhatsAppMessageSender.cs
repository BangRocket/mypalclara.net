using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MyPalClara.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Adapters.WhatsApp;

/// <summary>Sends messages via the WhatsApp Business Cloud API.</summary>
public sealed class WhatsAppMessageSender
{
    private readonly HttpClient _http;
    private readonly ClaraConfig _config;
    private readonly ILogger<WhatsAppMessageSender> _logger;

    private const string GraphApiBase = "https://graph.facebook.com/v21.0";

    public WhatsAppMessageSender(
        HttpClient http,
        ClaraConfig config,
        ILogger<WhatsAppMessageSender> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", config.WhatsApp.AccessToken);
    }

    /// <summary>Send a text message to a WhatsApp user.</summary>
    public async Task SendTextMessageAsync(string to, string text, CancellationToken ct = default)
    {
        var maxLen = _config.WhatsApp.MaxMessageLength;
        var chunks = SplitMessage(text, maxLen);

        foreach (var chunk in chunks)
        {
            var payload = new
            {
                messaging_product = "whatsapp",
                to,
                type = "text",
                text = new { body = chunk },
            };

            var json = JsonSerializer.Serialize(payload);
            var url = $"{GraphApiBase}/{_config.WhatsApp.PhoneNumberId}/messages";

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Failed to send WhatsApp message: {Status} {Error}",
                    response.StatusCode, error[..Math.Min(error.Length, 200)]);
            }
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
