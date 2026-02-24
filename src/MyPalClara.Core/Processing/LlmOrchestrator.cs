using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Core.Processing;

public class LlmOrchestrator
{
    private readonly ILlmProvider _llm;
    private readonly IToolRegistry _registry;
    private readonly ILogger<LlmOrchestrator> _logger;

    private readonly int _maxToolIterations;
    private readonly int _maxToolResultChars;
    private readonly int _textChunkSize;

    public LlmOrchestrator(
        ILlmProvider llm,
        IToolRegistry registry,
        ILogger<LlmOrchestrator> logger,
        int maxToolIterations = 75,
        int maxToolResultChars = 50_000,
        int textChunkSize = 50)
    {
        _llm = llm;
        _registry = registry;
        _logger = logger;
        _maxToolIterations = maxToolIterations;
        _maxToolResultChars = maxToolResultChars;
        _textChunkSize = textChunkSize;
    }

    public async IAsyncEnumerable<OrchestratorEvent> GenerateAsync(
        IReadOnlyList<LlmMessage> messages,
        ToolCallContext toolContext,
        string tier = "mid",
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var tools = _registry.GetAllTools();
        var conversationMessages = new List<LlmMessage>(messages);
        var totalToolCount = 0;
        var iteration = 0;
        string? lastTextContent = null;

        while (iteration < _maxToolIterations)
        {
            ct.ThrowIfCancellationRequested();

            var response = await _llm.InvokeAsync(conversationMessages, tools, ct);

            if (!response.HasToolCalls)
            {
                lastTextContent = response.Content ?? "";
                foreach (var chunk in ChunkText(lastTextContent))
                {
                    yield return new OrchestratorEvent.TextChunk(chunk);
                }
                break;
            }

            var assistantMsg = new AssistantMessage(
                Content: response.Content,
                ToolCalls: response.ToolCalls.ToList());
            conversationMessages.Add(assistantMsg);

            foreach (var toolCall in response.ToolCalls)
            {
                iteration++;
                totalToolCount++;

                yield return new OrchestratorEvent.ToolStart(toolCall.Name, iteration);

                _logger.LogInformation("Executing tool {Name} (step {Step})", toolCall.Name, iteration);

                var result = await _registry.ExecuteAsync(
                    toolCall.Name, toolCall.Arguments, toolContext, ct);

                var output = result.Output;
                if (output.Length > _maxToolResultChars)
                    output = output[.._maxToolResultChars] + "\n... [truncated]";

                var preview = output.Length > 150 ? output[..150] + "..." : output;
                yield return new OrchestratorEvent.ToolEnd(toolCall.Name, result.Success, preview);

                var content = result.Success ? output : $"Error: {result.Error}\n{output}";
                conversationMessages.Add(new ToolResultMessage(toolCall.Id, content));

                if (iteration >= _maxToolIterations)
                {
                    _logger.LogWarning("Max tool iterations ({Max}) reached", _maxToolIterations);
                    break;
                }
            }
        }

        var fullText = lastTextContent ?? "";
        yield return new OrchestratorEvent.Complete(fullText, totalToolCount);
    }

    private IEnumerable<string> ChunkText(string text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        for (var i = 0; i < text.Length; i += _textChunkSize)
        {
            var length = Math.Min(_textChunkSize, text.Length - i);
            yield return text.Substring(i, length);
        }
    }
}
