using System.Text.Json;
using Clara.Core.Llm;
using Clara.Core.SubAgents;
using Clara.Core.Tools;
using Clara.Core.Tools.BuiltIn;

namespace Clara.Core.Tests.Tools.BuiltIn;

public class SessionToolsTests
{
    private readonly ToolExecutionContext _context = new("user1", "clara:main:discord:guild:user1", "discord", false, null);

    [Fact]
    public async Task Spawn_returns_subtask_id()
    {
        var manager = new StubSubAgentManager("abc12345");
        var tool = new SessionsSpawnTool(manager);
        var args = JsonDocument.Parse("""{"task":"Summarize our conversation"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.True(result.Success);
        Assert.Contains("abc12345", result.Content);
    }

    [Fact]
    public async Task Spawn_with_tier_passes_tier_to_manager()
    {
        var manager = new StubSubAgentManager("xyz99999");
        var tool = new SessionsSpawnTool(manager);
        var args = JsonDocument.Parse("""{"task":"Complex analysis","tier":"high"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.True(result.Success);
        Assert.Equal(ModelTier.High, manager.LastRequest!.Tier);
    }

    [Fact]
    public async Task Spawn_missing_task_returns_failure()
    {
        var manager = new StubSubAgentManager("id");
        var tool = new SessionsSpawnTool(manager);
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.False(result.Success);
        Assert.Contains("task", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Spawn_max_reached_returns_failure()
    {
        var manager = new StubSubAgentManager("id", throwOnSpawn: true);
        var tool = new SessionsSpawnTool(manager);
        var args = JsonDocument.Parse("""{"task":"Another task"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.False(result.Success);
        Assert.Contains("Maximum", result.Error!);
    }

    // --- Stub ---

    private class StubSubAgentManager : ISubAgentManager
    {
        private readonly string _subTaskId;
        private readonly bool _throwOnSpawn;
        public SubAgentRequest? LastRequest { get; private set; }

        public StubSubAgentManager(string subTaskId, bool throwOnSpawn = false)
        {
            _subTaskId = subTaskId;
            _throwOnSpawn = throwOnSpawn;
        }

        public Task<string> SpawnAsync(SubAgentRequest request, CancellationToken ct = default)
        {
            if (_throwOnSpawn)
                throw new InvalidOperationException("Maximum sub-agents reached");

            LastRequest = request;
            return Task.FromResult(_subTaskId);
        }

        public Task<SubAgentResult?> GetResultAsync(string subTaskId, CancellationToken ct = default) =>
            Task.FromResult<SubAgentResult?>(null);

        public IReadOnlyList<string> GetActiveSubAgents(string parentSessionKey) =>
            Array.Empty<string>();

        public Task CancelAsync(string subTaskId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
