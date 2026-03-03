using System.Runtime.CompilerServices;
using Clara.Core.Config;
using Clara.Core.Events;
using Clara.Core.Llm;
using Clara.Core.Sessions;
using Clara.Core.SubAgents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clara.Core.Tests.SubAgents;

public class SubAgentManagerTests
{
    private const string ParentSessionKey = "clara:main:discord:guild:user1";

    private readonly StubLlmProvider _llmProvider = new("Sub-agent response text");
    private readonly StubLlmProviderFactory _providerFactory;
    private readonly StubSessionManager _sessionManager = new();
    private readonly ClaraEventBus _eventBus = new(NullLogger<ClaraEventBus>.Instance);
    private readonly SubAgentManager _manager;

    public SubAgentManagerTests()
    {
        _providerFactory = new StubLlmProviderFactory(_llmProvider);
        var options = Options.Create(new SubAgentOptions { MaxPerParent = 3 });
        _manager = new SubAgentManager(
            _providerFactory,
            _sessionManager,
            _eventBus,
            options,
            NullLogger<SubAgentManager>.Instance);
    }

    [Fact]
    public async Task Spawn_returns_subtask_id()
    {
        var request = new SubAgentRequest("Summarize this conversation", ParentSessionKey);

        var subTaskId = await _manager.SpawnAsync(request);

        Assert.NotNull(subTaskId);
        Assert.NotEmpty(subTaskId);
        Assert.Equal(8, subTaskId.Length);
    }

    [Fact]
    public async Task Spawn_enforces_max_per_parent()
    {
        var options = Options.Create(new SubAgentOptions { MaxPerParent = 2 });
        // Use a slow provider so the tasks don't complete immediately
        var slowProvider = new SlowLlmProvider();
        var factory = new StubLlmProviderFactory(slowProvider);
        var manager = new SubAgentManager(
            factory, _sessionManager, _eventBus, options,
            NullLogger<SubAgentManager>.Instance);

        await manager.SpawnAsync(new SubAgentRequest("Task 1", ParentSessionKey));
        await manager.SpawnAsync(new SubAgentRequest("Task 2", ParentSessionKey));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SpawnAsync(new SubAgentRequest("Task 3", ParentSessionKey)));

        Assert.Contains("Maximum sub-agents", ex.Message);
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public async Task GetActiveSubAgents_filters_by_parent()
    {
        // Use a slow provider so agents stay active
        var slowProvider = new SlowLlmProvider();
        var factory = new StubLlmProviderFactory(slowProvider);
        var options = Options.Create(new SubAgentOptions { MaxPerParent = 5 });
        var manager = new SubAgentManager(
            factory, _sessionManager, _eventBus, options,
            NullLogger<SubAgentManager>.Instance);

        var id1 = await manager.SpawnAsync(new SubAgentRequest("Task 1", ParentSessionKey));
        var id2 = await manager.SpawnAsync(new SubAgentRequest("Task 2", ParentSessionKey));
        await manager.SpawnAsync(new SubAgentRequest("Task 3", "clara:main:slack:guild:user2"));

        var active = manager.GetActiveSubAgents(ParentSessionKey);

        Assert.Equal(2, active.Count);
        Assert.Contains(id1, active);
        Assert.Contains(id2, active);
    }

    [Fact]
    public async Task Cancel_marks_agent_as_cancelled()
    {
        var slowProvider = new SlowLlmProvider();
        var factory = new StubLlmProviderFactory(slowProvider);
        var options = Options.Create(new SubAgentOptions { MaxPerParent = 5 });
        var manager = new SubAgentManager(
            factory, _sessionManager, _eventBus, options,
            NullLogger<SubAgentManager>.Instance);

        var subTaskId = await manager.SpawnAsync(new SubAgentRequest("Long task", ParentSessionKey));

        await manager.CancelAsync(subTaskId);

        // Wait a bit for the cancellation to propagate
        await Task.Delay(200);

        var result = await manager.GetResultAsync(subTaskId);
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Contains("Cancelled", result.Error);
    }

    [Fact]
    public async Task Result_available_after_completion()
    {
        var subTaskId = await _manager.SpawnAsync(
            new SubAgentRequest("Summarize this", ParentSessionKey));

        // Wait for the background task to complete
        await Task.Delay(200);

        var result = await _manager.GetResultAsync(subTaskId);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("Sub-agent response text", result.Content);
        Assert.Equal(subTaskId, result.SubTaskId);
    }

    [Fact]
    public async Task GetResult_returns_null_for_unknown_id()
    {
        var result = await _manager.GetResultAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task Completed_agent_publishes_event()
    {
        ClaraEvent? receivedEvent = null;
        _eventBus.Subscribe(SubAgentEvents.Completed, evt =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        });

        await _manager.SpawnAsync(new SubAgentRequest("Task", ParentSessionKey));

        // Wait for completion
        await Task.Delay(300);

        Assert.NotNull(receivedEvent);
        Assert.Equal(SubAgentEvents.Completed, receivedEvent!.Type);
        Assert.True((bool)receivedEvent.Data!["success"]);
    }

    // --- Stubs ---

    private class StubLlmProvider : ILlmProvider
    {
        private readonly string _response;
        public string Name => "stub";

        public StubLlmProvider(string response) => _response = response;

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new LlmResponse(
                [new TextContent(_response)],
                "EndTurn",
                new LlmUsage(10, 10)));
        }

        public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(
            LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new LlmStreamChunk { TextDelta = _response };
            await Task.CompletedTask;
        }
    }

    private class SlowLlmProvider : ILlmProvider
    {
        public string Name => "slow";

        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new LlmResponse(
                [new TextContent("done")],
                "EndTurn",
                new LlmUsage(10, 10));
        }

        public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(
            LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            yield return new LlmStreamChunk { TextDelta = "done" };
        }
    }

    private class StubLlmProviderFactory : ILlmProviderFactory
    {
        private readonly ILlmProvider _provider;

        public StubLlmProviderFactory(ILlmProvider provider) => _provider = provider;

        public ILlmProvider GetProvider(string? providerName = null) => _provider;
        public string ResolveModel(string providerName, ModelTier tier) => "test-model";
    }

    private class StubSessionManager : ISessionManager
    {
        public Task<Session> GetOrCreateAsync(string sessionKey, string? userId = null, CancellationToken ct = default)
        {
            return Task.FromResult(new Session
            {
                Id = Guid.NewGuid(),
                Key = SessionKey.Parse(sessionKey),
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            });
        }

        public Task<Session?> GetAsync(string sessionKey, CancellationToken ct = default) =>
            Task.FromResult<Session?>(null);

        public Task UpdateAsync(Session session, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task TimeoutAsync(string sessionKey, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
