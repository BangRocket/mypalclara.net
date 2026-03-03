using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Llm.Providers;

public class AnthropicProvider : ILlmProvider
{
    private readonly AnthropicApi _api;
    private readonly ILogger<AnthropicProvider> _logger;

    public string Name => "anthropic";

    public AnthropicProvider(string apiKey, ILogger<AnthropicProvider> logger)
    {
        _api = new AnthropicApi(new HttpClient(), new Uri("https://api.anthropic.com/v1/"));
        _api.AuthorizeUsingApiKey(apiKey);
        _logger = logger;
    }

    // Constructor for testing with injected API
    internal AnthropicProvider(AnthropicApi api, ILogger<AnthropicProvider> logger)
    {
        _api = api;
        _logger = logger;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var (system, sdkMessages) = ConvertMessages(request.Messages);

        var createRequest = new CreateMessageRequest
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens ?? 4096,
            Messages = sdkMessages,
        };

        if (!string.IsNullOrEmpty(system))
            createRequest.System = system;

        if (request.Tools is { Count: > 0 })
            createRequest.Tools = ConvertTools(request.Tools);

        var response = await _api.CreateMessageAsync(createRequest, ct);

        var content = ConvertResponseContent(response.Content);
        var usage = new LlmUsage(response.Usage?.InputTokens ?? 0, response.Usage?.OutputTokens ?? 0);
        var stopReason = response.StopReason?.ToString();

        return new LlmResponse(content, stopReason, usage);
    }

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (system, sdkMessages) = ConvertMessages(request.Messages);

        var createRequest = new CreateMessageRequest
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens ?? 4096,
            Messages = sdkMessages,
            Stream = true,
        };

        if (!string.IsNullOrEmpty(system))
            createRequest.System = system;

        if (request.Tools is { Count: > 0 })
            createRequest.Tools = ConvertTools(request.Tools);

        var stream = _api.CreateMessageAsStreamAsync(createRequest, ct);

        // Track tool use blocks being built incrementally
        string? currentToolId = null;
        string? currentToolName = null;
        string currentToolJson = "";

        await foreach (var evt in stream.WithCancellation(ct))
        {
            if (evt.IsContentBlockDelta)
            {
                var delta = evt.ContentBlockDelta;
                if (delta.Delta.IsValue1) // TextBlockDelta
                {
                    yield return new LlmStreamChunk { TextDelta = delta.Delta.Value1.Text };
                }
                else if (delta.Delta.IsValue2) // InputJsonBlockDelta
                {
                    currentToolJson += delta.Delta.Value2.PartialJson;
                }
            }
            else if (evt.IsContentBlockStart)
            {
                var blockStart = evt.ContentBlockStart;
                if (blockStart.ContentBlock.IsValue3) // ToolUseBlock
                {
                    var toolUse = blockStart.ContentBlock.Value3;
                    currentToolId = toolUse.Id;
                    currentToolName = toolUse.Name;
                    currentToolJson = "";
                }
            }
            else if (evt.IsContentBlockStop)
            {
                if (currentToolId is not null && currentToolName is not null)
                {
                    var args = string.IsNullOrEmpty(currentToolJson)
                        ? JsonDocument.Parse("{}").RootElement
                        : JsonDocument.Parse(currentToolJson).RootElement;

                    yield return new LlmStreamChunk
                    {
                        ToolCall = new ToolCallContent(currentToolId, currentToolName, args)
                    };

                    currentToolId = null;
                    currentToolName = null;
                    currentToolJson = "";
                }
            }
        }
    }

    // --- Conversion helpers (internal for testing) ---

    internal static (string? System, List<Message> Messages) ConvertMessages(
        IReadOnlyList<LlmMessage> messages)
    {
        string? system = null;
        var sdkMessages = new List<Message>();

        foreach (var msg in messages)
        {
            if (msg.Role == LlmRole.System)
            {
                // Concatenate system messages
                var texts = msg.Content.OfType<TextContent>().Select(t => t.Text);
                system = system is null
                    ? string.Join("\n", texts)
                    : system + "\n" + string.Join("\n", texts);
                continue;
            }

            var role = msg.Role switch
            {
                LlmRole.User => MessageRole.User,
                LlmRole.Assistant => MessageRole.Assistant,
                LlmRole.Tool => MessageRole.User, // Tool results are sent as user messages
                _ => MessageRole.User
            };

            var blocks = new List<Block>();

            foreach (var content in msg.Content)
            {
                switch (content)
                {
                    case TextContent tc:
                        blocks.Add(new TextBlock { Text = tc.Text });
                        break;

                    case ImageContent ic:
                        blocks.Add(new ImageBlock
                        {
                            Source = new ImageBlockSource
                            {
                                Type = ImageBlockSourceType.Base64,
                                Data = ic.Base64,
                                MediaType = Enum.Parse<ImageBlockSourceMediaType>(
                                    ic.MediaType.Replace("/", "_").Replace("image_", ""),
                                    ignoreCase: true),
                            }
                        });
                        break;

                    case ToolCallContent tcc:
                        blocks.Add(new ToolUseBlock
                        {
                            Id = tcc.Id,
                            Name = tcc.Name,
                            Input = tcc.Arguments
                        });
                        break;

                    case ToolResultContent trc:
                        blocks.Add(new ToolResultBlock
                        {
                            ToolUseId = trc.ToolCallId,
                            Content = trc.Content,
                            IsError = trc.IsError
                        });
                        break;
                }
            }

            sdkMessages.Add(new Message
            {
                Role = role,
                Content = new OneOf<string, IList<Block>>((IList<Block>)blocks)
            });
        }

        return (system, sdkMessages);
    }

    private static List<Tool> ConvertTools(IReadOnlyList<ToolDefinition> tools)
    {
        var result = new List<Tool>();
        foreach (var tool in tools)
        {
            result.Add(new Tool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = tool.ParameterSchema
            });
        }

        return result;
    }

    private static IReadOnlyList<LlmContent> ConvertResponseContent(
        OneOf<string, IList<Block>> content)
    {
        var result = new List<LlmContent>();

        if (content.IsValue1)
        {
            result.Add(new TextContent(content.Value1));
            return result;
        }

        foreach (var block in content.Value2!)
        {
            if (block.IsText)
            {
                result.Add(new TextContent(block.Text.Text));
            }
            else if (block.IsToolUse)
            {
                var toolUse = block.ToolUse;
                var args = JsonSerializer.SerializeToElement(toolUse.Input);
                result.Add(new ToolCallContent(toolUse.Id, toolUse.Name, args));
            }
        }

        return result;
    }
}
