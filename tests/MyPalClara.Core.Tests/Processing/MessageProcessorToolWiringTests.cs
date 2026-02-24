using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Core.Processing;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Core.Tests.Processing;

public class MessageProcessorToolWiringTests
{
    [Fact]
    public async Task Orchestrator_TextOnly_ToolCountIsZero()
    {
        var llm = new FakeLlm(new LlmResponse("hello", [], "end_turn"));
        var registry = new FakeRegistry([]);
        var orch = new LlmOrchestrator(llm, registry, NullLogger<LlmOrchestrator>.Instance);
        var ctx = new ToolCallContext("u", "c", "discord", "r");

        var events = new List<OrchestratorEvent>();
        await foreach (var e in orch.GenerateAsync([new UserMessage("hi")], ctx))
            events.Add(e);

        var complete = events.OfType<OrchestratorEvent.Complete>().Single();
        Assert.Equal(0, complete.ToolCount);
    }

    [Fact]
    public async Task Orchestrator_ToolThenText_ToolCountIsOne()
    {
        var tc = new ToolCall("t1", "test_tool", new());
        var q = new Queue<LlmResponse>();
        q.Enqueue(new LlmResponse(null, [tc], "tool_use"));
        q.Enqueue(new LlmResponse("done", [], "end_turn"));

        var llm = new FakeLlm(q);
        var registry = new FakeRegistry(
            [new ToolSchema("test_tool", "t", JsonDocument.Parse("{}").RootElement)]);
        var orch = new LlmOrchestrator(llm, registry, NullLogger<LlmOrchestrator>.Instance);
        var ctx = new ToolCallContext("u", "c", "discord", "r");

        var events = new List<OrchestratorEvent>();
        await foreach (var e in orch.GenerateAsync([new UserMessage("hi")], ctx))
            events.Add(e);

        var complete = events.OfType<OrchestratorEvent.Complete>().Single();
        Assert.Equal(1, complete.ToolCount);
        Assert.Equal("done", complete.FullText);
    }

    #region Fakes

    private class FakeLlm : ILlmProvider
    {
        private readonly Queue<LlmResponse> _q;

        public FakeLlm(LlmResponse single)
        {
            _q = new Queue<LlmResponse>();
            _q.Enqueue(single);
        }

        public FakeLlm(Queue<LlmResponse> q) => _q = q;

        public Task<LlmResponse> InvokeAsync(IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<ToolSchema>? tools = null, CancellationToken ct = default)
            => Task.FromResult(_q.Count > 0
                ? _q.Dequeue()
                : new LlmResponse("fallback", [], "end_turn"));

        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<LlmMessage> messages,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        { yield break; }
    }

    private class FakeRegistry : IToolRegistry
    {
        private readonly IReadOnlyList<ToolSchema> _tools;
        public FakeRegistry(IReadOnlyList<ToolSchema> tools) => _tools = tools;

        public void RegisterTool(string name, ToolSchema schema, Func<Dictionary<string, JsonElement>, ToolCallContext, CancellationToken, Task<ToolResult>> handler) { }
        public void RegisterSource(IToolSource source) { }
        public void UnregisterTool(string name) { }
        public IReadOnlyList<ToolSchema> GetAllTools(ToolFilter? filter = null) => _tools;

        public Task<ToolResult> ExecuteAsync(string name, Dictionary<string, JsonElement> args,
            ToolCallContext context, CancellationToken ct = default)
            => Task.FromResult(new ToolResult(true, "result"));
    }

    #endregion
}
