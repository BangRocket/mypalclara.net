using System.Text;
using System.Text.Json;
using Clara.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Llm;

/// <summary>
/// OpenAI-compatible chat completions client for the Rook provider.
/// Used by FactExtractor, TopicRecurrence, and FalkorDB entity extraction.
/// Rook is configured separately from the main Anthropic provider (memory.rook.* in clara.yaml).
/// Supports OpenAI, OpenRouter, NanoGPT, and any OpenAI-compatible endpoint.
/// </summary>
public sealed class RookProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<RookProvider> _logger;

    public RookProvider(HttpClient http, ClaraConfig config, ILogger<RookProvider> logger)
    {
        _http = http;
        _logger = logger;

        var rook = config.Memory.Rook;
        _model = !string.IsNullOrEmpty(rook.Model) ? rook.Model : "gpt-4o-mini";

        // Resolve base URL
        var baseUrl = !string.IsNullOrEmpty(rook.BaseUrl)
            ? rook.BaseUrl.TrimEnd('/')
            : "https://api.openai.com/v1";

        _http.BaseAddress = new Uri(baseUrl + "/");

        // API key: prefer rook-specific, fall back to llm.openai_api_key
        var apiKey = !string.IsNullOrEmpty(rook.ApiKey)
            ? rook.ApiKey
            : config.Llm.OpenaiApiKey;

        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    /// <summary>Chat completion using OpenAI-compatible format.</summary>
    public async Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        string? model = null,
        CancellationToken ct = default)
    {
        var resolvedModel = model ?? _model;

        var apiMessages = new List<object>();
        foreach (var msg in messages)
        {
            var role = msg switch
            {
                SystemMessage => "system",
                UserMessage => "user",
                AssistantMessage => "assistant",
                _ => "user",
            };
            apiMessages.Add(new { role, content = msg.Content ?? "" });
        }

        var body = new
        {
            model = resolvedModel,
            messages = apiMessages,
            temperature = 0,
            max_tokens = 8000,
        };

        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug("Rook request: model={Model}", resolvedModel);
        var response = await _http.PostAsync("chat/completions", content, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Rook API error {Status}: {Body}",
                response.StatusCode, responseBody[..Math.Min(500, responseBody.Length)]);
            throw new HttpRequestException(
                $"Rook API returned {response.StatusCode}: {responseBody[..Math.Min(500, responseBody.Length)]}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return "";

        return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
