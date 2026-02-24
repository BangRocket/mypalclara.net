using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Core.Processing;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Core.Tests.Processing;

public class LlmOrchestratorTests
{
    private readonly ToolCallContext _ctx = new("user-1", "ch-1", "discord", "req-1");

    [Fact]
    public async Task GenerateAsync_NoTools_YieldsTextChunksAndComplete()
    {
        var llm = new FakeLlmProvider(new LlmResponse("Hello world", [], "end_turn"));
        var registry = new FakeToolRegistry([]);
        var orchestrator = new LlmOrchestrator(llm, registry, NullLogger<LlmOrchestrator>.Instance);

        var events = new List<OrchestratorEvent>();
        await foreach (var evt in orchestrator.GenerateAsync(
            [new UserMessage("hi")], _ctx))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is OrchestratorEvent.TextChunk);
        Assert.Single(events.OfType<OrchestratorEvent.Complete>());

        var complete = events.OfType<OrchestratorEvent.Complete>().First();
        Assert.Equal("Hello world", complete.FullText);
        Assert.Equal(0, complete.ToolCount);
    }

    [Fact]
    public async Task GenerateAsync_WithToolCall_YieldsToolEvents()
    {
        var toolCall = new ToolCall("tc-1", "greet", new Dictionary<string, JsonElement>());
        var responses = new Queue<LlmResponse>();
        responses.Enqueue(new LlmResponse(null, [toolCall], "tool_use"));
        responses.Enqueue(new LlmResponse("Done!", [], "end_turn"));

        var llm = new FakeLlmProvider(responses);
        var registry = new FakeToolRegistry(
            [new ToolSchema("greet", "Greets", JsonDocument.Parse("{}").RootElement)],
            ctx => Task.FromResult(new ToolResult(true, "greeted")));

        var orchestrator = new LlmOrchestrator(llm, registry, NullLogger<LlmOrchestrator>.Instance);

        var events = new List<OrchestratorEvent>();
        await foreach (var evt in orchestrator.GenerateAsync(
            [new UserMessage("hi")], _ctx))
        {
            events.Add(evt);
        }

        Assert.Single(events.OfType<OrchestratorEvent.ToolStart>());
        Assert.Single(events.OfType<OrchestratorEvent.ToolEnd>());
        Assert.Single(events.OfType<OrchestratorEvent.Complete>());

        var complete = events.OfType<OrchestratorEvent.Complete>().First();
        Assert.Equal("Done!", complete.FullText);
        Assert.Equal(1, complete.ToolCount);
    }

    [Fact]
    public async Task GenerateAsync_MaxIterations_Stops()
    {
        var toolCall = new ToolCall("tc-1", "loop", new Dictionary<string, JsonElement>());
        var llm = new FakeLlmProvider(new LlmResponse(null, [toolCall], "tool_use"));
        var registry = new FakeToolRegistry(
            [new ToolSchema("loop", "Loops", JsonDocument.Parse("{}").RootElement)],
            ctx => Task.FromResult(new ToolResult(true, "looped")));

        var orchestrator = new LlmOrchestrator(llm, registry, NullLogger<LlmOrchestrator>.Instance,
            maxToolIterations: 3);

        var events = new List<OrchestratorEvent>();
        await foreach (var evt in orchestrator.GenerateAsync(
            [new UserMessage("hi")], _ctx))
        {
            events.Add(evt);
        }

        var toolStarts = events.OfType<OrchestratorEvent.ToolStart>().ToList();
        Assert.Equal(3, toolStarts.Count);
        Assert.Single(events.OfType<OrchestratorEvent.Complete>());
    }

    [Fact]
    public async Task GenerateAsync_ToolResultTruncation()
    {
        var toolCall = new ToolCall("tc-1", "big", new Dictionary<string, JsonElement>());
        var responses = new Queue<LlmResponse>();
        responses.Enqueue(new LlmResponse(null, [toolCall], "tool_use"));
        responses.Enqueue(new LlmResponse("ok", [], "end_turn"));

        var bigOutput = new string('x', 60_000);
        var llm = new FakeLlmProvider(responses);
        var registry = new FakeToolRegistry(
            [new ToolSchema("big", "Big output", JsonDocument.Parse("{}").RootElement)],
            ctx => Task.FromResult(new ToolResult(true, bigOutput)));

        var orchestrator = new LlmOrchestrator(llm, registry, NullLogger<LlmOrchestrator>.Instance,
            maxToolResultChars: 100);

        var events = new List<OrchestratorEvent>();
        await foreach (var evt in orchestrator.GenerateAsync(
            [new UserMessage("hi")], _ctx))
        {
            events.Add(evt);
        }

        var toolEnd = events.OfType<OrchestratorEvent.ToolEnd>().First();
        Assert.True(toolEnd.Preview.Length <= 200);
    }

    #region Fakes

    private class FakeLlmProvider : ILlmProvider
    {
        private readonly Queue<LlmResponse> _responses;

        public FakeLlmProvider(LlmResponse singleResponse)
        {
            _responses = new Queue<LlmResponse>();
            _responses.Enqueue(singleResponse);
        }

        public FakeLlmProvider(Queue<LlmResponse> responses)
        {
            _responses = responses;
        }

        public Task<LlmResponse> InvokeAsync(IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<ToolSchema>? tools = null, CancellationToken ct = default)
        {
            if (_responses.Count == 0)
                return Task.FromResult(new LlmResponse(null,
                    [new ToolCall("tc-loop", "loop", new Dictionary<string, JsonElement>())], "tool_use"));

            return Task.FromResult(_responses.Dequeue());
        }

        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<LlmMessage> messages,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "not used";
        }
    }

    private class FakeToolRegistry : IToolRegistry
    {
        private readonly IReadOnlyList<ToolSchema> _tools;
        private readonly Func<ToolCallContext, Task<ToolResult>>? _handler;

        public FakeToolRegistry(IReadOnlyList<ToolSchema> tools,
            Func<ToolCallContext, Task<ToolResult>>? handler = null)
        {
            _tools = tools;
            _handler = handler;
        }

        public void RegisterTool(string name, ToolSchema schema, Func<ToolCallContext, Task<ToolResult>> handler) { }
        public void RegisterSource(IToolSource source) { }
        public void UnregisterTool(string name) { }

        public IReadOnlyList<ToolSchema> GetAllTools(ToolFilter? filter = null) => _tools;

        public Task<ToolResult> ExecuteAsync(string name, Dictionary<string, JsonElement> args,
            ToolCallContext context, CancellationToken ct = default)
        {
            if (_handler is not null)
                return _handler(context);
            return Task.FromResult(new ToolResult(true, $"executed {name}"));
        }
    }

    #endregion
}
