using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Llm;
using MyPalClara.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Gateway.Orchestration;

/// <summary>
/// Multi-turn tool calling orchestrator. Port of mypalclara/gateway/llm_orchestrator.py.
/// Loops: LLM call → detect tool_use → execute via ToolExecutor → add results → repeat.
/// </summary>
public sealed class LlmOrchestrator
{
    private readonly ILlmProvider _llm;
    private readonly ToolExecutor _toolExecutor;
    private readonly ClaraConfig _config;
    private readonly ILogger<LlmOrchestrator> _logger;

    private static readonly Regex[] AutoContinuePatterns =
    [
        new(@"want me to .*\?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"should i .*\?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"shall i .*\?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"would you like me to .*\?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"ready to proceed\?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"proceed\?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"go ahead\?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"continue\?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"do you want me to .*\?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"i can .* if you('d)? like", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"let me know if", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    public LlmOrchestrator(
        ILlmProvider llm,
        ToolExecutor toolExecutor,
        ClaraConfig config,
        ILogger<LlmOrchestrator> logger)
    {
        _llm = llm;
        _toolExecutor = toolExecutor;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Generate response with multi-turn tool calling support.
    /// Yields events as they occur: TextChunk, ToolStart, ToolResult, Complete.
    /// </summary>
    public async IAsyncEnumerable<OrchestratorEvent> GenerateWithToolsAsync(
        List<ChatMessage> messages,
        IReadOnlyList<ToolSchema> tools,
        string? tier = null,
        int autoContinueCount = 0,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var maxIterations = _config.Gateway.MaxToolIterations;
        var maxResultChars = _config.Gateway.MaxToolResultChars;
        var model = _config.Llm.ModelForTier(tier);

        var workingMessages = new List<ChatMessage>(messages);
        int totalToolsRun = 0;

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            _logger.LogDebug("Iteration {Iter}/{Max}", iteration + 1, maxIterations);

            if (tools.Count > 0)
            {
                // Call LLM with tools (non-streaming to inspect tool calls)
                var toolResponse = await _llm.CompleteWithToolsAsync(
                    workingMessages, tools, model, ct: ct);

                if (!toolResponse.HasToolCalls)
                {
                    var content = toolResponse.Content ?? "";

                    // Check for auto-continue
                    var mayAutoContinue = _config.Gateway.AutoContinueEnabled
                        && autoContinueCount < _config.Gateway.AutoContinueMax;

                    if (mayAutoContinue && ShouldAutoContinue(content))
                    {
                        _logger.LogInformation("Auto-continue triggered (iteration {Count})",
                            autoContinueCount + 1);

                        // Stream what we have so far
                        foreach (var chunk in SimulateStream(content))
                            yield return new TextChunkEvent(chunk);

                        workingMessages.Add(new AssistantMessage(content));
                        workingMessages.Add(new UserMessage("Yes, please proceed."));

                        // Recurse
                        await foreach (var evt in GenerateWithToolsAsync(
                            workingMessages, tools, tier, autoContinueCount + 1, ct))
                        {
                            if (evt is CompleteEvent complete)
                            {
                                yield return new CompleteEvent(
                                    content + "\n\n" + complete.FullText,
                                    totalToolsRun + complete.ToolCount);
                            }
                            else
                            {
                                yield return evt;
                            }
                        }
                        yield break;
                    }

                    // No tools, no auto-continue — if first iteration, stream it
                    if (iteration == 0)
                    {
                        await foreach (var chunk in _llm.StreamAsync(
                            workingMessages, model, ct: ct))
                        {
                            content += chunk;
                            yield return new TextChunkEvent(chunk);
                        }
                    }
                    else
                    {
                        // Simulate streaming for post-tool responses
                        foreach (var chunk in SimulateStream(content))
                            yield return new TextChunkEvent(chunk);
                    }

                    yield return new CompleteEvent(content, totalToolsRun);
                    yield break;
                }

                // Process tool calls
                workingMessages.Add(toolResponse.ToAssistantMessage());

                foreach (var tc in toolResponse.ToolCalls)
                {
                    totalToolsRun++;
                    yield return new ToolStartEvent(tc.Name, totalToolsRun);

                    var output = await _toolExecutor.ExecuteAsync(tc, ct);

                    // Truncate if needed
                    if (output.Length > maxResultChars)
                        output = TruncateOutput(output, maxResultChars);

                    workingMessages.Add(new ToolResultMessage(tc.Id, output));

                    var success = !output.StartsWith("Error:");
                    var preview = output.Length > 200 ? output[..200] : output;
                    yield return new ToolResultEvent(tc.Name, success, preview);
                }
            }
            else
            {
                // No tools — just stream
                var content = "";
                await foreach (var chunk in _llm.StreamAsync(
                    workingMessages, model, ct: ct))
                {
                    content += chunk;
                    yield return new TextChunkEvent(chunk);
                }

                yield return new CompleteEvent(content, 0);
                yield break;
            }
        }

        // Max iterations reached
        _logger.LogWarning("Max iterations reached ({Max})", maxIterations);
        workingMessages.Add(new UserMessage(
            "You've reached the maximum number of tool calls. Please summarize what you've accomplished."));

        var finalContent = await _llm.CompleteAsync(workingMessages, model, ct: ct);

        foreach (var chunk in SimulateStream(finalContent))
            yield return new TextChunkEvent(chunk);

        yield return new CompleteEvent(finalContent, totalToolsRun);
    }

    private static bool ShouldAutoContinue(string response)
    {
        if (string.IsNullOrEmpty(response)) return false;
        var tail = response.Length > 200 ? response[^200..] : response;
        return AutoContinuePatterns.Any(p => p.IsMatch(tail));
    }

    private static IEnumerable<string> SimulateStream(string text, int chunkSize = 50)
    {
        var words = text.Split(' ');
        var current = new List<string>();
        var currentLen = 0;

        foreach (var word in words)
        {
            current.Add(word);
            currentLen += word.Length + 1;

            if (currentLen >= chunkSize)
            {
                yield return string.Join(' ', current) + " ";
                current.Clear();
                currentLen = 0;
            }
        }

        if (current.Count > 0)
            yield return string.Join(' ', current);
    }

    private static string TruncateOutput(string output, int maxChars)
    {
        var truncated = output[..maxChars];
        return $"{truncated}\n\n[TRUNCATED: Result was {output.Length:N0} chars, showing first {maxChars:N0}. " +
               "Use pagination parameters or more specific filters to get smaller results.]";
    }
}
