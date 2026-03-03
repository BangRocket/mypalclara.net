using System.Runtime.CompilerServices;
using Clara.Core.Llm.ToolCalling;
using Clara.Core.Tools;
using Clara.Core.Tools.ToolPolicy;

namespace Clara.Core.Llm;

public class LlmOrchestrator
{
    private readonly IToolRegistry _tools;
    private readonly ToolPolicyPipeline _policyPipeline;
    private readonly int _maxToolRounds;

    public LlmOrchestrator(IToolRegistry tools, ToolPolicyPipeline policyPipeline, int maxToolRounds = 10)
    {
        _tools = tools;
        _policyPipeline = policyPipeline;
        _maxToolRounds = maxToolRounds;
    }

    public async IAsyncEnumerable<OrchestratorEvent> RunAsync(
        ILlmProvider provider,
        LlmRequest initialRequest,
        ToolExecutionContext toolContext,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = initialRequest.Messages.ToList();
        var loopDetector = new ToolLoopDetector(maxTotalRounds: _maxToolRounds);
        var round = 0;

        while (round < _maxToolRounds)
        {
            var request = initialRequest with { Messages = messages };
            var response = await provider.CompleteAsync(request, ct);

            // Yield text content
            foreach (var content in response.Content.OfType<TextContent>())
                yield return new TextDelta(content.Text);

            // Check for tool calls
            var toolCalls = ToolCallParser.ExtractToolCalls(response);
            if (toolCalls.Count == 0)
                yield break; // No tool calls -> final response, done

            // Process tool calls — add assistant message with all content
            var assistantContent = new List<LlmContent>(response.Content);
            messages.Add(new LlmMessage(LlmRole.Assistant, assistantContent));

            foreach (var call in toolCalls)
            {
                var argsJson = call.Arguments.GetRawText();

                // Loop detection
                if (loopDetector.IsLoop(call.Name, argsJson))
                {
                    yield return new LoopDetected(call.Name, round);
                    yield break;
                }

                loopDetector.Record(call.Name, argsJson, round);

                // Policy check
                if (!_policyPipeline.IsAllowed(call.Name, toolContext))
                {
                    messages.Add(new LlmMessage(LlmRole.Tool,
                        [new ToolResultContent(call.Id, "Tool denied by policy", IsError: true)]));
                    continue;
                }

                // Resolve tool
                var tool = _tools.Resolve(call.Name);
                if (tool is null)
                {
                    messages.Add(new LlmMessage(LlmRole.Tool,
                        [new ToolResultContent(call.Id, $"Unknown tool: {call.Name}", IsError: true)]));
                    continue;
                }

                yield return new ToolStarted(call.Name, argsJson);
                var result = await tool.ExecuteAsync(call.Arguments, toolContext, ct);
                yield return new ToolCompleted(call.Name, result);

                var resultContent = result.Success ? result.Content : $"Error: {result.Error}";
                messages.Add(new LlmMessage(LlmRole.Tool,
                    [new ToolResultContent(call.Id, resultContent, !result.Success)]));
            }

            round++;
        }

        yield return new MaxRoundsReached(_maxToolRounds);
    }
}
