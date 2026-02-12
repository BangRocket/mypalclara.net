using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clara.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Llm;

/// <summary>
/// OpenAI-compatible chat completions provider implementing ILlmProvider.
/// Works with OpenAI, OpenRouter, NanoGPT, and any OpenAI-compatible endpoint.
/// Supports non-streaming, streaming (SSE), and function-calling tool use.
/// </summary>
public sealed class OpenAiProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly ClaraConfig _config;
    private readonly LlmCallLogger _callLogger;
    private readonly ILogger<OpenAiProvider> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public OpenAiProvider(HttpClient http, ClaraConfig config, LlmCallLogger callLogger, ILogger<OpenAiProvider> logger)
    {
        _http = http;
        _config = config;
        _callLogger = callLogger;
        _logger = logger;

        var provider = config.Llm.ActiveProvider;
        var baseUrl = !string.IsNullOrEmpty(provider.BaseUrl)
            ? provider.BaseUrl.TrimEnd('/')
            : "https://api.openai.com/v1";

        _http.BaseAddress = new Uri(baseUrl + "/");

        var apiKey = !string.IsNullOrEmpty(provider.ApiKey)
            ? provider.ApiKey
            : config.Llm.OpenaiApiKey;

        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Clara/1.0");

        // OpenRouter-specific headers
        if (config.Llm.Provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(provider.Site))
                _http.DefaultRequestHeaders.Add("HTTP-Referer", provider.Site);
            if (!string.IsNullOrEmpty(provider.Title))
                _http.DefaultRequestHeaders.Add("X-Title", provider.Title);
        }
    }

    public async Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        string? model = null,
        int maxTokens = 4096,
        float temperature = 0f,
        CancellationToken ct = default)
    {
        var body = BuildRequestBody(messages, model, maxTokens, temperature, tools: null, stream: false);
        var doc = await PostAsync(body, ct);
        return ExtractContent(doc);
    }

    public async Task<ToolResponse> CompleteWithToolsAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolSchema> tools,
        string? model = null,
        int maxTokens = 4096,
        float temperature = 0f,
        CancellationToken ct = default)
    {
        var body = BuildRequestBody(messages, model, maxTokens, temperature, tools, stream: false);
        var doc = await PostAsync(body, ct);
        return ParseToolResponse(doc);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string? model = null,
        int maxTokens = 4096,
        float temperature = 0f,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(messages, model, maxTokens, temperature, tools: null, stream: true);
        var json = JsonSerializer.Serialize(body, JsonOpts);

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (!line.StartsWith("data: ")) continue;
            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            using var doc = JsonDocument.Parse(data);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) continue;

            var delta = choices[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var content))
            {
                var text = content.GetString();
                if (text is not null) yield return text;
            }
        }
    }

    private Dictionary<string, object?> BuildRequestBody(
        IReadOnlyList<ChatMessage> messages,
        string? model,
        int maxTokens,
        float temperature,
        IReadOnlyList<ToolSchema>? tools,
        bool stream)
    {
        var resolvedModel = model ?? _config.Llm.ActiveProvider.Model;

        var apiMessages = new List<object>();

        foreach (var msg in messages)
        {
            switch (msg)
            {
                case SystemMessage sys when sys.Content is not null:
                    apiMessages.Add(new { role = "system", content = sys.Content });
                    break;

                case UserMessage user:
                    apiMessages.Add(new { role = "user", content = user.Content ?? "" });
                    break;

                case AssistantMessage asst:
                    if (asst.ToolCalls is { Count: > 0 })
                    {
                        var toolCalls = asst.ToolCalls.Select(tc => new
                        {
                            id = tc.Id,
                            type = "function",
                            function = new
                            {
                                name = tc.Name,
                                arguments = tc.Arguments.GetRawText(),
                            },
                        }).ToList();

                        apiMessages.Add(new
                        {
                            role = "assistant",
                            content = asst.Content ?? (object?)null,
                            tool_calls = toolCalls,
                        });
                    }
                    else
                    {
                        apiMessages.Add(new { role = "assistant", content = asst.Content ?? "" });
                    }
                    break;

                case ToolResultMessage toolResult:
                    apiMessages.Add(new
                    {
                        role = "tool",
                        tool_call_id = toolResult.ToolCallId,
                        content = toolResult.Content ?? "",
                    });
                    break;
            }
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = resolvedModel,
            ["max_tokens"] = maxTokens,
            ["temperature"] = temperature,
            ["messages"] = apiMessages,
        };

        if (tools is { Count: > 0 })
            body["tools"] = tools.Select(t => t.ToOpenAiFormat()).ToList();

        if (stream)
            body["stream"] = true;

        return body;
    }

    private async Task<JsonDocument> PostAsync(Dictionary<string, object?> body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        var model = body["model"]?.ToString() ?? "unknown";
        var providerName = _config.Llm.Provider.ToLowerInvariant();
        _logger.LogDebug("OpenAI request: {Provider}/{Model}", providerName, model);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("chat/completions", content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI API error {Status}: {Body}",
                response.StatusCode, responseBody[..Math.Min(500, responseBody.Length)]);

            _ = _callLogger.LogCallAsync(
                conversationId: null, model, provider: providerName,
                requestBody: json, responseBody: responseBody,
                latencyMs: (int)sw.ElapsedMilliseconds,
                status: "error",
                errorMessage: $"{response.StatusCode}",
                ct: ct);

            throw new HttpRequestException(
                $"OpenAI-compatible API returned {response.StatusCode}: {responseBody[..Math.Min(500, responseBody.Length)]}");
        }

        // Extract token usage
        int? inputTokens = null, outputTokens = null;
        try
        {
            using var usageDoc = JsonDocument.Parse(responseBody);
            if (usageDoc.RootElement.TryGetProperty("usage", out var usage))
            {
                inputTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : null;
                outputTokens = usage.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt32() : null;
            }
        }
        catch { /* don't fail the call over logging */ }

        _ = _callLogger.LogCallAsync(
            conversationId: null, model, provider: providerName,
            requestBody: json, responseBody: responseBody,
            inputTokens: inputTokens, outputTokens: outputTokens,
            latencyMs: (int)sw.ElapsedMilliseconds,
            status: "success",
            ct: ct);

        return JsonDocument.Parse(responseBody);
    }

    private static string ExtractContent(JsonDocument doc)
    {
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0) return "";
        return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    private static ToolResponse ParseToolResponse(JsonDocument doc)
    {
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return new ToolResponse(null, [], null);

        var choice = choices[0];
        var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
        var message = choice.GetProperty("message");

        var textContent = message.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null
            ? c.GetString()
            : null;

        var toolCalls = new List<ToolCall>();

        if (message.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in tc.EnumerateArray())
            {
                var id = call.GetProperty("id").GetString()!;
                var fn = call.GetProperty("function");
                var name = fn.GetProperty("name").GetString()!;
                var argsStr = fn.GetProperty("arguments").GetString() ?? "{}";

                // OpenAI returns arguments as a JSON string, parse it to JsonElement
                using var argsDoc = JsonDocument.Parse(argsStr);
                toolCalls.Add(new ToolCall(id, name, argsDoc.RootElement.Clone()));
            }
        }

        return new ToolResponse(textContent, toolCalls, finishReason);
    }
}
