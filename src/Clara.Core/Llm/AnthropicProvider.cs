using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clara.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Llm;

/// <summary>
/// Raw HttpClient implementation against the Anthropic Messages API.
/// Supports non-streaming, streaming (SSE), and tool-calling modes.
/// </summary>
public sealed class AnthropicProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly ClaraConfig _config;
    private readonly ILogger<AnthropicProvider> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public AnthropicProvider(HttpClient http, ClaraConfig config, ILogger<AnthropicProvider> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;

        var provider = config.Llm.ActiveProvider;
        var baseUrl = provider.BaseUrl.TrimEnd('/');
        _http.BaseAddress = new Uri(baseUrl.EndsWith("/v1") ? baseUrl + "/" : baseUrl + "/v1/");

        _http.DefaultRequestHeaders.Add("x-api-key", provider.ApiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Clara/1.0");

        // Cloudflare Access headers
        var cf = config.Llm.CloudflareAccess;
        if (!string.IsNullOrEmpty(cf.ClientId))
        {
            _http.DefaultRequestHeaders.Add("CF-Access-Client-Id", cf.ClientId);
            _http.DefaultRequestHeaders.Add("CF-Access-Client-Secret", cf.ClientSecret);
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
        var response = await PostAsync(body, ct);
        return ExtractTextContent(response);
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
        var response = await PostAsync(body, ct);
        return ParseToolResponse(response);
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "messages")
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
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            if (type == "content_block_delta")
            {
                var delta = root.GetProperty("delta");
                if (delta.GetProperty("type").GetString() == "text_delta")
                {
                    var text = delta.GetProperty("text").GetString();
                    if (text is not null) yield return text;
                }
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

        // Separate system messages from conversation messages
        var systemParts = new List<string>();
        var conversationMessages = new List<object>();

        foreach (var msg in messages)
        {
            switch (msg)
            {
                case SystemMessage sys when sys.Content is not null:
                    systemParts.Add(sys.Content);
                    break;

                case UserMessage user:
                    conversationMessages.Add(new { role = "user", content = user.Content ?? "" });
                    break;

                case AssistantMessage asst:
                    if (asst.ToolCalls is { Count: > 0 })
                    {
                        var content = new List<object>();
                        if (!string.IsNullOrEmpty(asst.Content))
                            content.Add(new { type = "text", text = asst.Content });
                        foreach (var tc in asst.ToolCalls)
                            content.Add(new { type = "tool_use", id = tc.Id, name = tc.Name, input = tc.Arguments });
                        conversationMessages.Add(new { role = "assistant", content });
                    }
                    else
                    {
                        conversationMessages.Add(new { role = "assistant", content = asst.Content ?? "" });
                    }
                    break;

                case ToolResultMessage toolResult:
                    conversationMessages.Add(new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "tool_result", tool_use_id = toolResult.ToolCallId, content = toolResult.Content ?? "" }
                        }
                    });
                    break;
            }
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = resolvedModel,
            ["max_tokens"] = maxTokens,
            ["temperature"] = temperature,
            ["messages"] = conversationMessages,
        };

        if (systemParts.Count > 0)
            body["system"] = string.Join("\n\n", systemParts);

        if (tools is { Count: > 0 })
            body["tools"] = tools.Select(t => t.ToAnthropicFormat()).ToList();

        if (stream)
            body["stream"] = true;

        return body;
    }

    private async Task<JsonDocument> PostAsync(Dictionary<string, object?> body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        _logger.LogDebug("Anthropic request: {Model}", body["model"]);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("messages", content, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Anthropic API error {Status}: {Body}",
                response.StatusCode, responseBody[..Math.Min(500, responseBody.Length)]);
            throw new HttpRequestException(
                $"Anthropic API returned {response.StatusCode}: {responseBody[..Math.Min(500, responseBody.Length)]}");
        }

        return JsonDocument.Parse(responseBody);
    }

    private static string ExtractTextContent(JsonDocument doc)
    {
        var sb = new StringBuilder();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "text")
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(block.GetProperty("text").GetString());
            }
        }
        return sb.ToString();
    }

    private static ToolResponse ParseToolResponse(JsonDocument doc)
    {
        var root = doc.RootElement;
        var stopReason = root.GetProperty("stop_reason").GetString();

        string? textContent = null;
        var toolCalls = new List<ToolCall>();

        foreach (var block in root.GetProperty("content").EnumerateArray())
        {
            var type = block.GetProperty("type").GetString();
            if (type == "text")
            {
                textContent = block.GetProperty("text").GetString();
            }
            else if (type == "tool_use")
            {
                toolCalls.Add(new ToolCall(
                    Id: block.GetProperty("id").GetString()!,
                    Name: block.GetProperty("name").GetString()!,
                    Arguments: block.GetProperty("input").Clone()));
            }
        }

        return new ToolResponse(textContent, toolCalls, stopReason);
    }
}
