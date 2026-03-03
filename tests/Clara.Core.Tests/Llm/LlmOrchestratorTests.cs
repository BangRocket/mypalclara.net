using System.Text.Json;
using Clara.Core.Llm;
using Clara.Core.Llm.ToolCalling;
using Clara.Core.Tools;
using Clara.Core.Tools.ToolPolicy;

namespace Clara.Core.Tests.Llm;

public class LlmOrchestratorTests
{
    private static readonly ToolExecutionContext TestContext = new("user1", "session1", "test", false, null);

    private static ToolPolicyPipeline AllowAll() => new([]);
    private static ToolPolicyPipeline DenyTool(string toolName) =>
        new([new DenySpecificPolicy(toolName)]);

    [Fact]
    public async Task Text_only_response_yields_TextDelta_and_completes()
    {
        var provider = new SequencedProvider(
            TextResponse("Hello, world!"));
        var registry = new ToolRegistry();
        var orchestrator = new LlmOrchestrator(registry, AllowAll());

        var events = await CollectEvents(orchestrator, provider);

        Assert.Single(events);
        var textDelta = Assert.IsType<TextDelta>(events[0]);
        Assert.Equal("Hello, world!", textDelta.Text);
    }

    [Fact]
    public async Task Tool_call_executed_then_text_response()
    {
        // Round 1: LLM returns tool call → ToolStarted + ToolCompleted
        // Round 2: LLM returns text → TextDelta
        var provider = new SequencedProvider(
            ToolCallResponse("call_1", "echo_tool", """{"text":"hi"}"""),
            TextResponse("Done!"));

        var registry = new ToolRegistry();
        registry.Register(new EchoTool());
        var orchestrator = new LlmOrchestrator(registry, AllowAll());

        var events = await CollectEvents(orchestrator, provider);

        Assert.Equal(3, events.Count);
        Assert.IsType<ToolStarted>(events[0]);
        Assert.Equal("echo_tool", ((ToolStarted)events[0]).ToolName);
        Assert.IsType<ToolCompleted>(events[1]);
        Assert.True(((ToolCompleted)events[1]).Result.Success);
        Assert.Contains("hi", ((ToolCompleted)events[1]).Result.Content);
        Assert.IsType<TextDelta>(events[2]);
        Assert.Equal("Done!", ((TextDelta)events[2]).Text);
    }

    [Fact]
    public async Task Unknown_tool_returns_error_to_LLM()
    {
        var provider = new SequencedProvider(
            ToolCallResponse("call_1", "nonexistent_tool", "{}"),
            TextResponse("I see the error"));

        var registry = new ToolRegistry(); // No tools registered
        var orchestrator = new LlmOrchestrator(registry, AllowAll());

        var events = await CollectEvents(orchestrator, provider);

        // No ToolStarted/ToolCompleted (tool not found), just text from second call
        var textEvent = events.OfType<TextDelta>().Single();
        Assert.Equal("I see the error", textEvent.Text);
    }

    [Fact]
    public async Task Policy_denied_tool_returns_denial_to_LLM()
    {
        var provider = new SequencedProvider(
            ToolCallResponse("call_1", "echo_tool", """{"text":"hi"}"""),
            TextResponse("Tool was denied"));

        var registry = new ToolRegistry();
        registry.Register(new EchoTool());
        var orchestrator = new LlmOrchestrator(registry, DenyTool("echo_tool"));

        var events = await CollectEvents(orchestrator, provider);

        // No ToolStarted/ToolCompleted since denied
        Assert.DoesNotContain(events, e => e is ToolStarted);
        Assert.DoesNotContain(events, e => e is ToolCompleted);
        var textEvent = events.OfType<TextDelta>().Single();
        Assert.Equal("Tool was denied", textEvent.Text);
    }

    [Fact]
    public async Task Loop_detection_triggers_LoopDetected_event()
    {
        // Same tool call repeated — will trigger loop after 3 identical
        var provider = new SequencedProvider(
            ToolCallResponse("c1", "echo_tool", """{"text":"same"}"""),
            ToolCallResponse("c2", "echo_tool", """{"text":"same"}"""),
            ToolCallResponse("c3", "echo_tool", """{"text":"same"}"""),
            ToolCallResponse("c4", "echo_tool", """{"text":"same"}"""));

        var registry = new ToolRegistry();
        registry.Register(new EchoTool());
        var orchestrator = new LlmOrchestrator(registry, AllowAll(), maxToolRounds: 10);

        var events = await CollectEvents(orchestrator, provider);

        Assert.Contains(events, e => e is LoopDetected);
    }

    [Fact]
    public async Task Max_rounds_triggers_MaxRoundsReached_event()
    {
        // Each round returns a tool call, never text-only
        var responses = Enumerable.Range(0, 5)
            .Select(i => ToolCallResponse($"c{i}", "echo_tool", $$$"""{"text":"round{{{i}}}"}"""))
            .ToArray();
        var provider = new SequencedProvider(responses);

        var registry = new ToolRegistry();
        registry.Register(new EchoTool());
        var orchestrator = new LlmOrchestrator(registry, AllowAll(), maxToolRounds: 3);

        var events = await CollectEvents(orchestrator, provider);

        Assert.Contains(events, e => e is MaxRoundsReached);
        var maxEvent = events.OfType<MaxRoundsReached>().Single();
        Assert.Equal(3, maxEvent.MaxRounds);
    }

    // --- Test helpers ---

    private static async Task<List<OrchestratorEvent>> CollectEvents(
        LlmOrchestrator orchestrator, ILlmProvider provider)
    {
        var request = new LlmRequest("test-model", [LlmMessage.User("test")]);
        var events = new List<OrchestratorEvent>();
        await foreach (var evt in orchestrator.RunAsync(provider, request, TestContext))
            events.Add(evt);
        return events;
    }

    private static LlmResponse TextResponse(string text) =>
        new([new TextContent(text)], "end_turn", new LlmUsage(10, 5));

    private static LlmResponse ToolCallResponse(string callId, string toolName, string argsJson) =>
        new([new ToolCallContent(callId, toolName, JsonDocument.Parse(argsJson).RootElement)],
            "tool_use", new LlmUsage(10, 5));

    private class SequencedProvider : ILlmProvider
    {
        private readonly LlmResponse[] _responses;
        private int _index;

        public SequencedProvider(params LlmResponse[] responses) => _responses = responses;

        public string Name => "test";

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
        {
            if (_index >= _responses.Length)
                return Task.FromResult(TextResponse("(end)"));
            return Task.FromResult(_responses[_index++]);
        }

        public IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        private static LlmResponse TextResponse(string text) =>
            new([new TextContent(text)], "end_turn", new LlmUsage(0, 0));
    }

    private class EchoTool : ITool
    {
        public string Name => "echo_tool";
        public string Description => "Echoes text back";
        public ToolCategory Category => ToolCategory.Shell;
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;

        public Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
        {
            var text = arguments.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            return Task.FromResult(ToolResult.Ok($"Echo: {text}"));
        }
    }

    private class DenySpecificPolicy : IToolPolicy
    {
        private readonly string _toolName;
        public DenySpecificPolicy(string toolName) => _toolName = toolName;
        public int Priority => 1;
        public ToolPolicyDecision Evaluate(string toolName, ToolExecutionContext context) =>
            string.Equals(toolName, _toolName, StringComparison.OrdinalIgnoreCase)
                ? ToolPolicyDecision.Deny
                : ToolPolicyDecision.Abstain;
    }
}
