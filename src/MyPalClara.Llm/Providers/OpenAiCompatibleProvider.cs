using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MyPalClara.Llm.Providers;

/// <summary>
/// OpenAI-compatible chat completions provider. Works with OpenRouter, NanoGPT,
/// custom OpenAI endpoints, and Azure OpenAI.
/// Thread-safe: uses a shared HttpClient instance.
/// </summary>
public sealed class OpenAiCompatibleProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly LlmConfig _config;
    private readonly string _completionsUrl;
    private readonly bool _isAzure;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiCompatibleProvider(LlmConfig config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;
        _isAzure = string.Equals(config.Provider, "azure", StringComparison.OrdinalIgnoreCase);

        if (_isAzure)
        {
            var endpoint = config.BaseUrl?.TrimEnd('/') ??
                throw new InvalidOperationException("Azure endpoint (BaseUrl) is required.");
            var deployment = config.AzureDeploymentName ??
                throw new InvalidOperationException("Azure deployment name is required.");
            var apiVersion = config.AzureApiVersion ?? "2024-02-15-preview";
            _completionsUrl = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
        }
        else
        {
            var baseUrl = config.BaseUrl?.TrimEnd('/') ?? "https://api.openai.com/v1";
            _completionsUrl = $"{baseUrl}/chat/completions";
        }
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
                $"OpenAI-compatible API returned {(int)response.StatusCode}: {responseBody}");
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
                $"OpenAI-compatible API returned {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();

            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            var text = ExtractStreamingDelta(data);
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

        var apiMessages = new JsonArray();
        foreach (var msg in messages)
        {
            apiMessages.Add(FormatMessage(msg));
        }
        body["messages"] = apiMessages;

        if (tools is { Count: > 0 })
        {
            var toolsArray = new JsonArray();
            foreach (var tool in tools)
            {
                toolsArray.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.Parameters.GetRawText())
                    }
                });
            }
            body["tools"] = toolsArray;
        }

        return body;
    }

    private static JsonNode FormatMessage(LlmMessage msg)
    {
        switch (msg)
        {
            case SystemMessage sys:
                return new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = sys.Content
                };

            case UserMessage user when user.Parts is null or { Count: 0 }:
                return new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = user.Content
                };

            case UserMessage user:
            {
                var contentArray = new JsonArray();
                foreach (var part in user.Parts!)
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
                                ["type"] = "image_url",
                                ["image_url"] = new JsonObject
                                {
                                    ["url"] = $"data:{part.MediaType};base64,{part.Base64Data}"
                                }
                            });
                            break;
                        case ContentPartType.ImageUrl:
                            contentArray.Add(new JsonObject
                            {
                                ["type"] = "image_url",
                                ["image_url"] = new JsonObject
                                {
                                    ["url"] = part.Url
                                }
                            });
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(user.Content) &&
                    !user.Parts!.Any(p => p.Type == ContentPartType.Text))
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

            case AssistantMessage asst when asst.ToolCalls is null or { Count: 0 }:
                return new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = asst.Content ?? ""
                };

            case AssistantMessage asst:
            {
                var toolCallsArray = new JsonArray();
                foreach (var tc in asst.ToolCalls!)
                {
                    // OpenAI format: arguments is a JSON string
                    var argsObj = new JsonObject();
                    foreach (var kvp in tc.Arguments)
                    {
                        argsObj[kvp.Key] = JsonNode.Parse(kvp.Value.GetRawText());
                    }

                    toolCallsArray.Add(new JsonObject
                    {
                        ["id"] = tc.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = tc.Name,
                            ["arguments"] = argsObj.ToJsonString()
                        }
                    });
                }

                var assistantObj = new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = asst.Content ?? ""
                };
                assistantObj["tool_calls"] = toolCallsArray;
                return assistantObj;
            }

            case ToolResultMessage tool:
                return new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = tool.ToolCallId,
                    ["content"] = tool.Content
                };

            default:
                throw new ArgumentException($"Unknown message type: {msg.GetType().Name}");
        }
    }

    private HttpRequestMessage CreateHttpRequest(JsonObject body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _completionsUrl)
        {
            Content = new StringContent(
                body.ToJsonString(JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        if (_isAzure)
        {
            request.Headers.Add("api-key", _config.ApiKey);
        }
        else
        {
            request.Headers.Add("Authorization", $"Bearer {_config.ApiKey}");
        }

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

        var choices = root.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
        {
            return new LlmResponse(null, [], null);
        }

        var choice = choices[0];
        var message = choice.GetProperty("message");

        // Extract text content
        string? content = null;
        if (message.TryGetProperty("content", out var contentEl) &&
            contentEl.ValueKind == JsonValueKind.String)
        {
            content = contentEl.GetString();
        }

        // Extract tool calls
        var toolCalls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCallsEl))
        {
            foreach (var tc in toolCallsEl.EnumerateArray())
            {
                var id = tc.GetProperty("id").GetString()!;
                var function = tc.GetProperty("function");
                var name = function.GetProperty("name").GetString()!;
                var argsString = function.GetProperty("arguments").GetString()!;

                var args = new Dictionary<string, JsonElement>();
                using var argsDoc = JsonDocument.Parse(argsString);
                foreach (var prop in argsDoc.RootElement.EnumerateObject())
                {
                    args[prop.Name] = prop.Value.Clone();
                }

                toolCalls.Add(new ToolCall(id, name, args));
            }
        }

        // Extract finish reason
        string? stopReason = null;
        if (choice.TryGetProperty("finish_reason", out var fr) &&
            fr.ValueKind == JsonValueKind.String)
        {
            stopReason = fr.GetString();
        }

        return new LlmResponse(content, toolCalls, stopReason);
    }

    private static string? ExtractStreamingDelta(string jsonData)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
                return null;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
                return null;

            if (delta.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
