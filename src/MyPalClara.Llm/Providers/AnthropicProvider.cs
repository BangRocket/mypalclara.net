using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MyPalClara.Llm.Providers;

/// <summary>
/// Direct HTTP implementation of the Anthropic Messages API.
/// Thread-safe: uses a shared HttpClient instance.
/// </summary>
public sealed class AnthropicProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly LlmConfig _config;
    private readonly string _messagesUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicProvider(LlmConfig config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;

        var baseUrl = config.BaseUrl?.TrimEnd('/') ?? "https://api.anthropic.com";
        _messagesUrl = $"{baseUrl}/v1/messages";
    }

    public async Task<LlmResponse> InvokeAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolSchema>? tools = null,
        CancellationToken ct = default)
    {
        var requestBody = BuildRequestBody(messages, tools, stream: false);
        using var request = CreateHttpRequest(requestBody);

        using var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Anthropic API returned {(int)response.StatusCode}: {responseBody}");
        }

        return ParseResponse(responseBody);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var requestBody = BuildRequestBody(messages, tools: null, stream: true);
        using var request = CreateHttpRequest(requestBody);

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Anthropic API returned {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();

            // SSE format: "event: <type>" followed by "data: <json>"
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            var text = ExtractStreamingText(data);
            if (text is not null)
                yield return text;
        }
    }

    private JsonObject BuildRequestBody(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolSchema>? tools,
        bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = _config.Model,
            ["max_tokens"] = _config.MaxTokens,
            ["temperature"] = _config.Temperature
        };

        if (stream)
            body["stream"] = true;

        // Extract system message (goes in top-level field, not messages array)
        string? systemContent = null;
        var apiMessages = new JsonArray();

        // Track consecutive tool results to batch them into a single user message
        var pendingToolResults = new List<LlmMessage>();

        foreach (var msg in messages)
        {
            if (msg is SystemMessage sys)
            {
                systemContent = sys.Content;
                continue;
            }

            // If we have pending tool results and this isn't another tool result, flush them
            if (pendingToolResults.Count > 0 && msg is not ToolResultMessage)
            {
                apiMessages.Add(BuildToolResultsUserMessage(pendingToolResults));
                pendingToolResults.Clear();
            }

            switch (msg)
            {
                case UserMessage user:
                    apiMessages.Add(FormatUserMessage(user));
                    break;
                case AssistantMessage asst:
                    apiMessages.Add(FormatAssistantMessage(asst));
                    break;
                case ToolResultMessage:
                    pendingToolResults.Add(msg);
                    break;
            }
        }

        // Flush any remaining tool results
        if (pendingToolResults.Count > 0)
        {
            apiMessages.Add(BuildToolResultsUserMessage(pendingToolResults));
        }

        if (systemContent is not null)
            body["system"] = systemContent;

        body["messages"] = apiMessages;

        if (tools is { Count: > 0 })
        {
            var toolsArray = new JsonArray();
            foreach (var tool in tools)
            {
                var toolObj = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["input_schema"] = JsonNode.Parse(tool.Parameters.GetRawText())
                };
                toolsArray.Add(toolObj);
            }
            body["tools"] = toolsArray;
        }

        return body;
    }

    private static JsonObject FormatUserMessage(UserMessage user)
    {
        if (user.Parts is null or { Count: 0 })
        {
            return new JsonObject
            {
                ["role"] = "user",
                ["content"] = user.Content
            };
        }

        // Multimodal content
        var contentArray = new JsonArray();
        foreach (var part in user.Parts)
        {
            switch (part.Type)
            {
                case ContentPartType.Text:
                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = part.Text
                    });
                    break;
                case ContentPartType.ImageBase64:
                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = part.MediaType,
                            ["data"] = part.Base64Data
                        }
                    });
                    break;
                case ContentPartType.ImageUrl:
                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "url",
                            ["url"] = part.Url
                        }
                    });
                    break;
            }
        }

        // If there's also a Content string and no text part was in Parts, add it
        if (!string.IsNullOrEmpty(user.Content) &&
            !user.Parts.Any(p => p.Type == ContentPartType.Text))
        {
            contentArray.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = user.Content
            });
        }

        return new JsonObject
        {
            ["role"] = "user",
            ["content"] = contentArray
        };
    }

    private static JsonObject FormatAssistantMessage(AssistantMessage asst)
    {
        if (asst.ToolCalls is null or { Count: 0 })
        {
            return new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = asst.Content ?? ""
            };
        }

        // Assistant message with tool calls uses content array
        var contentArray = new JsonArray();

        if (!string.IsNullOrEmpty(asst.Content))
        {
            contentArray.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = asst.Content
            });
        }

        foreach (var tc in asst.ToolCalls)
        {
            var inputObj = new JsonObject();
            foreach (var kvp in tc.Arguments)
            {
                inputObj[kvp.Key] = JsonNode.Parse(kvp.Value.GetRawText());
            }

            contentArray.Add(new JsonObject
            {
                ["type"] = "tool_use",
                ["id"] = tc.Id,
                ["name"] = tc.Name,
                ["input"] = inputObj
            });
        }

        return new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = contentArray
        };
    }

    private static JsonObject BuildToolResultsUserMessage(List<LlmMessage> toolResults)
    {
        var contentArray = new JsonArray();
        foreach (var msg in toolResults)
        {
            if (msg is ToolResultMessage tr)
            {
                contentArray.Add(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = tr.ToolCallId,
                    ["content"] = tr.Content
                });
            }
        }

        return new JsonObject
        {
            ["role"] = "user",
            ["content"] = contentArray
        };
    }

    private HttpRequestMessage CreateHttpRequest(JsonObject body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _messagesUrl)
        {
            Content = new StringContent(
                body.ToJsonString(JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Add("x-api-key", _config.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        if (_config.ExtraHeaders is not null)
        {
            foreach (var (key, value) in _config.ExtraHeaders)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        return request;
    }

    private static LlmResponse ParseResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        string? textContent = null;
        var toolCalls = new List<ToolCall>();

        if (root.TryGetProperty("content", out var contentArray))
        {
            foreach (var block in contentArray.EnumerateArray())
            {
                var blockType = block.GetProperty("type").GetString();

                if (blockType == "text")
                {
                    textContent = block.GetProperty("text").GetString();
                }
                else if (blockType == "tool_use")
                {
                    var id = block.GetProperty("id").GetString()!;
                    var name = block.GetProperty("name").GetString()!;
                    var input = block.GetProperty("input");

                    var args = new Dictionary<string, JsonElement>();
                    foreach (var prop in input.EnumerateObject())
                    {
                        args[prop.Name] = prop.Value.Clone();
                    }

                    toolCalls.Add(new ToolCall(id, name, args));
                }
            }
        }

        var stopReason = root.TryGetProperty("stop_reason", out var sr)
            ? sr.GetString()
            : null;

        return new LlmResponse(textContent, toolCalls, stopReason);
    }

    private static string? ExtractStreamingText(string jsonData)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            var eventType = root.TryGetProperty("type", out var t)
                ? t.GetString()
                : null;

            // content_block_delta carries the actual text tokens
            if (eventType == "content_block_delta")
            {
                if (root.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("type", out var deltaType) &&
                    deltaType.GetString() == "text_delta" &&
                    delta.TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
