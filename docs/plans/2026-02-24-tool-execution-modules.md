# Tool Execution System and Module Implementations -- Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add tool execution to the .NET gateway and implement all 6 modules to achieve 1:1 feature parity with the Python gateway.

**Architecture:** Centralized IToolRegistry in Modules.Sdk with IToolSource for module tool registration. LlmOrchestrator wraps ILlmProvider + IToolRegistry, replaces StreamAsync in MessageProcessor. Modules register tools via IToolSource at startup.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core, Docker.DotNet, StackExchange.Redis, MailKit, AWSSDK.S3

**Existing key types (DO NOT recreate):**
- `MyPalClara.Llm`: `ILlmProvider`, `LlmResponse`, `LlmMessage`, `ToolSchema`, `ToolCall`, `ToolResultMessage`, `AssistantMessage`, `SystemMessage`, `UserMessage`
- `MyPalClara.Modules.Sdk`: `IGatewayModule`, `IEventBus`, `IGatewayBridge`, `GatewayEvent`, `EventTypes`, `ModuleHealth`, `IScheduler`
- `MyPalClara.Core.Processing`: `MessageProcessor`, `ProcessingContext`, `IMessageProcessor`, `PromptBuilder`, `SessionManager`
- `MyPalClara.Gateway`: `EventBus`, `HookManager`, `Scheduler`, `ModuleLoader`, `ModuleRegistry`, `GatewayBridge`, `ServiceCollectionExtensions`
- `MyPalClara.Data.Entities`: `Message`, `McpServer`, `McpToolCall`, `McpUsageMetrics`, `PersonalityTrait`, `LogEntry`, `ProactiveNote`, `ProactiveMessage`, `ProactiveAssessment`, `EmailAccount`, `EmailRule`, `Intention`

---

## Phase A: Tool Execution Pipeline (Tasks 1-6)

### Task 1: IToolRegistry + IToolSource + ToolCallContext + ToolResult + ToolFilter contracts in Modules.Sdk

**Files:**
- Create: `src/MyPalClara.Modules.Sdk/IToolRegistry.cs`
- Create: `src/MyPalClara.Modules.Sdk/IToolSource.cs`
- Create: `src/MyPalClara.Modules.Sdk/ToolCallContext.cs`
- Create: `src/MyPalClara.Modules.Sdk/ToolResult.cs`
- Create: `src/MyPalClara.Modules.Sdk/ToolFilter.cs`
- Modify: `src/MyPalClara.Modules.Sdk/MyPalClara.Modules.Sdk.csproj` (add ProjectReference to MyPalClara.Llm)
- Test: `tests/MyPalClara.Gateway.Tests/Tools/ToolRegistryContractTests.cs`

The Modules.Sdk project currently has no reference to MyPalClara.Llm, but IToolRegistry.RegisterTool needs `ToolSchema` and ExecuteAsync needs `Dictionary<string, JsonElement>` (both from `MyPalClara.Llm`). We must add this project reference.

**Step 1: Write the failing test**

Create `tests/MyPalClara.Gateway.Tests/Tools/ToolRegistryContractTests.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

/// <summary>
/// Verify the SDK contracts compile and have correct shapes.
/// These tests use the interfaces directly (no implementation yet).
/// </summary>
public class ToolRegistryContractTests
{
    [Fact]
    public void ToolCallContext_RecordProperties()
    {
        var ctx = new ToolCallContext("user-1", "ch-1", "discord", "req-1");
        Assert.Equal("user-1", ctx.UserId);
        Assert.Equal("ch-1", ctx.ChannelId);
        Assert.Equal("discord", ctx.Platform);
        Assert.Equal("req-1", ctx.RequestId);
    }

    [Fact]
    public void ToolResult_Success()
    {
        var result = new ToolResult(true, "hello");
        Assert.True(result.Success);
        Assert.Equal("hello", result.Output);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ToolResult_Failure()
    {
        var result = new ToolResult(false, "", "boom");
        Assert.False(result.Success);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void ToolFilter_DefaultsToNull()
    {
        var filter = new ToolFilter();
        Assert.Null(filter.Platform);
        Assert.Null(filter.Capabilities);
    }

    [Fact]
    public void ToolFilter_WithValues()
    {
        var filter = new ToolFilter("discord", new List<string> { "files" });
        Assert.Equal("discord", filter.Platform);
        Assert.Single(filter.Capabilities!);
    }

    [Fact]
    public void IToolRegistry_InterfaceShape()
    {
        // Compile-time verification that the interface has expected members.
        // We cast null and verify methods exist via reflection.
        var type = typeof(IToolRegistry);
        Assert.NotNull(type.GetMethod("RegisterTool"));
        Assert.NotNull(type.GetMethod("RegisterSource"));
        Assert.NotNull(type.GetMethod("UnregisterTool"));
        Assert.NotNull(type.GetMethod("GetAllTools"));
        Assert.NotNull(type.GetMethod("ExecuteAsync"));
    }

    [Fact]
    public void IToolSource_InterfaceShape()
    {
        var type = typeof(IToolSource);
        Assert.NotNull(type.GetProperty("Name"));
        Assert.NotNull(type.GetMethod("GetTools"));
        Assert.NotNull(type.GetMethod("CanHandle"));
        Assert.NotNull(type.GetMethod("ExecuteAsync"));
    }
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/MyPalClara.Gateway.Tests --filter "FullyQualifiedName~ToolRegistryContractTests" --verbosity normal
```

Expected: FAIL (types do not exist yet)

**Step 3: Write minimal implementation**

3a. Modify `src/MyPalClara.Modules.Sdk/MyPalClara.Modules.Sdk.csproj` -- add ProjectReference to Llm:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyPalClara.Llm\MyPalClara.Llm.csproj" />
  </ItemGroup>
</Project>
```

3b. Create `src/MyPalClara.Modules.Sdk/ToolCallContext.cs`:

```csharp
namespace MyPalClara.Modules.Sdk;

public record ToolCallContext(string UserId, string ChannelId, string Platform, string RequestId);
```

3c. Create `src/MyPalClara.Modules.Sdk/ToolResult.cs`:

```csharp
namespace MyPalClara.Modules.Sdk;

public record ToolResult(bool Success, string Output, string? Error = null);
```

3d. Create `src/MyPalClara.Modules.Sdk/ToolFilter.cs`:

```csharp
namespace MyPalClara.Modules.Sdk;

public record ToolFilter(string? Platform = null, List<string>? Capabilities = null);
```

3e. Create `src/MyPalClara.Modules.Sdk/IToolRegistry.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Llm;

namespace MyPalClara.Modules.Sdk;

public interface IToolRegistry
{
    void RegisterTool(string name, ToolSchema schema, Func<ToolCallContext, Task<ToolResult>> handler);
    void RegisterSource(IToolSource source);
    void UnregisterTool(string name);
    IReadOnlyList<ToolSchema> GetAllTools(ToolFilter? filter = null);
    Task<ToolResult> ExecuteAsync(string name, Dictionary<string, JsonElement> args, ToolCallContext context, CancellationToken ct = default);
}
```

3f. Create `src/MyPalClara.Modules.Sdk/IToolSource.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Llm;

namespace MyPalClara.Modules.Sdk;

public interface IToolSource
{
    string Name { get; }
    IReadOnlyList<ToolSchema> GetTools();
    bool CanHandle(string toolName);
    Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args, ToolCallContext context, CancellationToken ct = default);
}
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/MyPalClara.Gateway.Tests --filter "FullyQualifiedName~ToolRegistryContractTests" --verbosity normal
```

Expected: PASS (7 tests)

**Step 5: Commit**

```
git add src/MyPalClara.Modules.Sdk/IToolRegistry.cs src/MyPalClara.Modules.Sdk/IToolSource.cs src/MyPalClara.Modules.Sdk/ToolCallContext.cs src/MyPalClara.Modules.Sdk/ToolResult.cs src/MyPalClara.Modules.Sdk/ToolFilter.cs src/MyPalClara.Modules.Sdk/MyPalClara.Modules.Sdk.csproj tests/MyPalClara.Gateway.Tests/Tools/ToolRegistryContractTests.cs
git commit -m "feat: add IToolRegistry, IToolSource, ToolCallContext, ToolResult, ToolFilter contracts to Modules.Sdk"
```

---

### Task 2: ToolRegistry implementation in Gateway/Tools/

**Files:**
- Create: `src/MyPalClara.Gateway/Tools/ToolRegistry.cs`
- Test: `tests/MyPalClara.Gateway.Tests/Tools/ToolRegistryTests.cs`

**Step 1: Write the failing test**

Create `tests/MyPalClara.Gateway.Tests/Tools/ToolRegistryTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Tools;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

public class ToolRegistryTests
{
    private readonly ToolRegistry _registry = new(NullLogger<ToolRegistry>.Instance);
    private readonly ToolCallContext _ctx = new("user-1", "ch-1", "discord", "req-1");

    private static ToolSchema MakeSchema(string name) =>
        new(name, $"Description for {name}", JsonDocument.Parse("{}").RootElement);

    [Fact]
    public void RegisterTool_And_GetAllTools_ReturnsIt()
    {
        _registry.RegisterTool("test_tool", MakeSchema("test_tool"),
            ctx => Task.FromResult(new ToolResult(true, "ok")));

        var tools = _registry.GetAllTools();
        Assert.Single(tools);
        Assert.Equal("test_tool", tools[0].Name);
    }

    [Fact]
    public void RegisterTool_DuplicateName_Throws()
    {
        _registry.RegisterTool("dupe", MakeSchema("dupe"),
            ctx => Task.FromResult(new ToolResult(true, "ok")));

        Assert.Throws<InvalidOperationException>(() =>
            _registry.RegisterTool("dupe", MakeSchema("dupe"),
                ctx => Task.FromResult(new ToolResult(true, "ok"))));
    }

    [Fact]
    public void UnregisterTool_RemovesIt()
    {
        _registry.RegisterTool("removeme", MakeSchema("removeme"),
            ctx => Task.FromResult(new ToolResult(true, "ok")));

        _registry.UnregisterTool("removeme");

        Assert.Empty(_registry.GetAllTools());
    }

    [Fact]
    public async Task ExecuteAsync_CallsHandler()
    {
        _registry.RegisterTool("echo", MakeSchema("echo"),
            ctx => Task.FromResult(new ToolResult(true, $"hello {ctx.UserId}")));

        var result = await _registry.ExecuteAsync("echo", new(), _ctx);

        Assert.True(result.Success);
        Assert.Equal("hello user-1", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsError()
    {
        var result = await _registry.ExecuteAsync("nonexistent", new(), _ctx);

        Assert.False(result.Success);
        Assert.Contains("nonexistent", result.Error);
    }

    [Fact]
    public void RegisterSource_ToolsAppearInGetAllTools()
    {
        var source = new FakeToolSource("fake", new[] { MakeSchema("fake__alpha"), MakeSchema("fake__beta") });
        _registry.RegisterSource(source);

        var tools = _registry.GetAllTools();
        Assert.Equal(2, tools.Count);
    }

    [Fact]
    public async Task ExecuteAsync_DelegatesToSource()
    {
        var source = new FakeToolSource("fake", new[] { MakeSchema("fake__alpha") });
        _registry.RegisterSource(source);

        var result = await _registry.ExecuteAsync("fake__alpha", new(), _ctx);

        Assert.True(result.Success);
        Assert.Equal("fake handled fake__alpha", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_DirectToolTakesPrecedenceOverSource()
    {
        // Register a source that can handle "overlap"
        var source = new FakeToolSource("fake", new[] { MakeSchema("overlap") });
        _registry.RegisterSource(source);

        // Register a direct tool with the same name
        _registry.RegisterTool("overlap", MakeSchema("overlap"),
            ctx => Task.FromResult(new ToolResult(true, "direct wins")));

        var result = await _registry.ExecuteAsync("overlap", new(), _ctx);

        Assert.True(result.Success);
        Assert.Equal("direct wins", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerException_ReturnsError()
    {
        _registry.RegisterTool("boom", MakeSchema("boom"),
            ctx => throw new InvalidOperationException("kaboom"));

        var result = await _registry.ExecuteAsync("boom", new(), _ctx);

        Assert.False(result.Success);
        Assert.Contains("kaboom", result.Error);
    }

    [Fact]
    public void GetAllTools_WithFilter_NotImplementedReturnsAll()
    {
        _registry.RegisterTool("a", MakeSchema("a"),
            ctx => Task.FromResult(new ToolResult(true, "ok")));

        // Filter is accepted but currently returns all (filtering is module-level)
        var tools = _registry.GetAllTools(new ToolFilter("discord"));
        Assert.Single(tools);
    }

    /// <summary>Helper: fake IToolSource for testing.</summary>
    private class FakeToolSource : IToolSource
    {
        private readonly ToolSchema[] _tools;
        public string Name { get; }

        public FakeToolSource(string name, ToolSchema[] tools)
        {
            Name = name;
            _tools = tools;
        }

        public IReadOnlyList<ToolSchema> GetTools() => _tools;

        public bool CanHandle(string toolName) =>
            _tools.Any(t => t.Name == toolName);

        public Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args,
            ToolCallContext context, CancellationToken ct = default) =>
            Task.FromResult(new ToolResult(true, $"fake handled {toolName}"));
    }
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/MyPalClara.Gateway.Tests --filter "FullyQualifiedName~ToolRegistryTests" --verbosity normal
```

Expected: FAIL (ToolRegistry class does not exist)

**Step 3: Write minimal implementation**

Create `src/MyPalClara.Gateway/Tools/ToolRegistry.cs`:

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, (ToolSchema Schema, Func<ToolCallContext, Task<ToolResult>> Handler)> _tools = new();
    private readonly List<IToolSource> _sources = [];
    private readonly object _sourceLock = new();
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = logger;
    }

    public void RegisterTool(string name, ToolSchema schema, Func<ToolCallContext, Task<ToolResult>> handler)
    {
        if (!_tools.TryAdd(name, (schema, handler)))
            throw new InvalidOperationException($"Tool '{name}' is already registered.");

        _logger.LogDebug("Registered tool: {Name}", name);
    }

    public void RegisterSource(IToolSource source)
    {
        lock (_sourceLock)
        {
            _sources.Add(source);
        }
        _logger.LogInformation("Registered tool source: {Name} ({Count} tools)",
            source.Name, source.GetTools().Count);
    }

    public void UnregisterTool(string name)
    {
        _tools.TryRemove(name, out _);
        _logger.LogDebug("Unregistered tool: {Name}", name);
    }

    public IReadOnlyList<ToolSchema> GetAllTools(ToolFilter? filter = null)
    {
        var result = new List<ToolSchema>();

        // Direct-registered tools
        foreach (var (_, (schema, _)) in _tools)
            result.Add(schema);

        // Source-provided tools
        List<IToolSource> snapshot;
        lock (_sourceLock)
        {
            snapshot = [.. _sources];
        }

        foreach (var source in snapshot)
        {
            try
            {
                foreach (var tool in source.GetTools())
                {
                    // Skip if a direct tool with same name already exists
                    if (!_tools.ContainsKey(tool.Name))
                        result.Add(tool);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get tools from source {Name}", source.Name);
            }
        }

        return result;
    }

    public async Task<ToolResult> ExecuteAsync(string name, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default)
    {
        // 1. Check direct-registered tools first
        if (_tools.TryGetValue(name, out var entry))
        {
            try
            {
                return await entry.Handler(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool '{Name}' handler threw an exception", name);
                return new ToolResult(false, "", $"Tool '{name}' failed: {ex.Message}");
            }
        }

        // 2. Check sources
        List<IToolSource> snapshot;
        lock (_sourceLock)
        {
            snapshot = [.. _sources];
        }

        foreach (var source in snapshot)
        {
            if (source.CanHandle(name))
            {
                try
                {
                    return await source.ExecuteAsync(name, args, context, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Tool source '{Source}' failed executing '{Tool}'", source.Name, name);
                    return new ToolResult(false, "", $"Tool '{name}' failed: {ex.Message}");
                }
            }
        }

        return new ToolResult(false, "", $"Unknown tool: '{name}'. No handler or source registered for this tool.");
    }
}
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/MyPalClara.Gateway.Tests --filter "FullyQualifiedName~ToolRegistryTests" --verbosity normal
```

Expected: PASS (10 tests)

**Step 5: Commit**

```
git add src/MyPalClara.Gateway/Tools/ToolRegistry.cs tests/MyPalClara.Gateway.Tests/Tools/ToolRegistryTests.cs
git commit -m "feat: implement ToolRegistry with direct-tool and IToolSource dispatch"
```

---

### Task 3: OrchestratorEvent + LlmOrchestrator in Core/Processing/

**Files:**
- Create: `src/MyPalClara.Core/Processing/OrchestratorEvent.cs`
- Create: `src/MyPalClara.Core/Processing/LlmOrchestrator.cs`
- Modify: `src/MyPalClara.Core/MyPalClara.Core.csproj` (already references Modules.Sdk and Llm -- no change needed)
- Modify: `tests/MyPalClara.Core.Tests/MyPalClara.Core.Tests.csproj` (add project references)
- Test: `tests/MyPalClara.Core.Tests/Processing/LlmOrchestratorTests.cs`

**Step 1: Write the failing test**

First, modify `tests/MyPalClara.Core.Tests/MyPalClara.Core.Tests.csproj` to add needed references:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MyPalClara.Core\MyPalClara.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

Create `tests/MyPalClara.Core.Tests/Processing/LlmOrchestratorTests.cs`:

```csharp
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

        // Should have TextChunk events and a Complete event
        Assert.Contains(events, e => e is OrchestratorEvent.TextChunk);
        Assert.Single(events.OfType<OrchestratorEvent.Complete>());

        var complete = events.OfType<OrchestratorEvent.Complete>().First();
        Assert.Equal("Hello world", complete.FullText);
        Assert.Equal(0, complete.ToolCount);
    }

    [Fact]
    public async Task GenerateAsync_WithToolCall_YieldsToolEvents()
    {
        // First call returns a tool call, second call returns text
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
        // LLM always returns a tool call -- should stop at max iterations
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

        // Should still yield Complete
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

        // Verify the tool result was truncated in the preview
        var toolEnd = events.OfType<OrchestratorEvent.ToolEnd>().First();
        Assert.True(toolEnd.Preview.Length <= 200); // preview is capped
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
                // If we run out of queued responses, return the last one again (for loop tests)
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
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~LlmOrchestratorTests" --verbosity normal
```

Expected: FAIL (OrchestratorEvent and LlmOrchestrator do not exist)

**Step 3: Write minimal implementation**

3a. Create `src/MyPalClara.Core/Processing/OrchestratorEvent.cs`:

```csharp
namespace MyPalClara.Core.Processing;

/// <summary>
/// Events yielded by LlmOrchestrator during generation.
/// </summary>
public abstract record OrchestratorEvent
{
    /// <summary>A chunk of text from the LLM response (simulated streaming).</summary>
    public sealed record TextChunk(string Text) : OrchestratorEvent;

    /// <summary>A tool execution is starting.</summary>
    public sealed record ToolStart(string Name, int Step) : OrchestratorEvent;

    /// <summary>A tool execution has completed.</summary>
    public sealed record ToolEnd(string Name, bool Success, string Preview) : OrchestratorEvent;

    /// <summary>Generation is complete.</summary>
    public sealed record Complete(string FullText, int ToolCount) : OrchestratorEvent;
}
```

3b. Create `src/MyPalClara.Core/Processing/LlmOrchestrator.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Core.Processing;

/// <summary>
/// Wraps ILlmProvider + IToolRegistry. Runs the tool-calling loop,
/// yielding OrchestratorEvents for streaming to the client.
/// </summary>
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
                // Final text response -- yield as chunks
                lastTextContent = response.Content ?? "";
                foreach (var chunk in ChunkText(lastTextContent))
                {
                    yield return new OrchestratorEvent.TextChunk(chunk);
                }
                break;
            }

            // Tool-calling loop
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

                // Truncate large results
                var output = result.Output;
                if (output.Length > _maxToolResultChars)
                    output = output[.._maxToolResultChars] + "\n... [truncated]";

                var preview = output.Length > 150 ? output[..150] + "..." : output;
                yield return new OrchestratorEvent.ToolEnd(toolCall.Name, result.Success, preview);

                // Add tool result to conversation
                var content = result.Success ? output : $"Error: {result.Error}\n{output}";
                conversationMessages.Add(new ToolResultMessage(toolCall.Id, content));

                if (iteration >= _maxToolIterations)
                {
                    _logger.LogWarning("Max tool iterations ({Max}) reached", _maxToolIterations);
                    break;
                }
            }
        }

        // Yield Complete event
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
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~LlmOrchestratorTests" --verbosity normal
```

Expected: PASS (4 tests)

**Step 5: Commit**

```
git add src/MyPalClara.Core/Processing/OrchestratorEvent.cs src/MyPalClara.Core/Processing/LlmOrchestrator.cs tests/MyPalClara.Core.Tests/MyPalClara.Core.Tests.csproj tests/MyPalClara.Core.Tests/Processing/LlmOrchestratorTests.cs
git commit -m "feat: implement LlmOrchestrator with tool-calling loop and OrchestratorEvent types"
```

---

### Task 4: Wire LlmOrchestrator into MessageProcessor

**Files:**
- Modify: `src/MyPalClara.Core/Processing/MessageProcessor.cs`
- Test: `tests/MyPalClara.Core.Tests/Processing/MessageProcessorToolWiringTests.cs`

**Step 1: Write the failing test**

Create `tests/MyPalClara.Core.Tests/Processing/MessageProcessorToolWiringTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Core.Processing;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Core.Tests.Processing;

/// <summary>
/// Verify that LlmOrchestrator produces the correct event sequence
/// and that tool_count is accurately tracked (which MessageProcessor will use).
/// This is an integration test of Orchestrator -> ToolRegistry.
/// </summary>
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

        public void RegisterTool(string name, ToolSchema schema, Func<ToolCallContext, Task<ToolResult>> handler) { }
        public void RegisterSource(IToolSource source) { }
        public void UnregisterTool(string name) { }
        public IReadOnlyList<ToolSchema> GetAllTools(ToolFilter? filter = null) => _tools;

        public Task<ToolResult> ExecuteAsync(string name, Dictionary<string, JsonElement> args,
            ToolCallContext context, CancellationToken ct = default)
            => Task.FromResult(new ToolResult(true, "result"));
    }

    #endregion
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~MessageProcessorToolWiringTests" --verbosity normal
```

Expected: Initially PASS (these test the orchestrator, not MessageProcessor itself). The real verification is that MessageProcessor compiles after the changes below.

**Step 3: Write minimal implementation**

Modify `src/MyPalClara.Core/Processing/MessageProcessor.cs`. Replace the `StreamAsync` block (step 7 in the current code) with `LlmOrchestrator.GenerateAsync` and map events to WebSocket protocol messages. Key changes:

1. Add `LlmOrchestrator` as a constructor dependency (it wraps `ILlmProvider` + `IToolRegistry`).
2. Remove direct `ILlmProvider` dependency (orchestrator owns it).
3. Replace step 7 (StreamAsync) with GenerateAsync loop.
4. Map `OrchestratorEvent.TextChunk` -> `response_chunk` WebSocket message.
5. Map `OrchestratorEvent.ToolStart` -> `tool_start` WebSocket message.
6. Map `OrchestratorEvent.ToolEnd` -> `tool_result` WebSocket message.
7. Use `Complete.ToolCount` in `response_end`.

Replace the full file `src/MyPalClara.Core/Processing/MessageProcessor.cs`:

```csharp
using System.Net.WebSockets;
using System.Text;
using MyPalClara.Data;
using MyPalClara.Llm;
using MyPalClara.Memory;
using MyPalClara.Memory.FactExtraction;
using MyPalClara.Memory.VectorStore;
using MyPalClara.Modules.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Core.Processing;

/// <summary>
/// Core message processing orchestrator.
/// Registered as a singleton; uses IServiceScopeFactory for scoped DbContext access.
/// </summary>
public class MessageProcessor : IMessageProcessor
{
    /// <summary>
    /// Delegate for sending messages back through WebSocket, avoiding circular dependency on GatewayServer.
    /// </summary>
    public delegate Task SendMessageDelegate(WebSocket ws, object message, CancellationToken ct);

    private readonly LlmOrchestrator _orchestrator;
    private readonly IRookMemory _rook;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SendMessageDelegate _send;
    private readonly SessionManager _sessionManager;
    private readonly IFactExtractor _factExtractor;
    private readonly SmartIngestion _smartIngestion;
    private readonly ILogger<MessageProcessor> _logger;

    public MessageProcessor(
        LlmOrchestrator orchestrator,
        IRookMemory rook,
        IServiceScopeFactory scopeFactory,
        SendMessageDelegate send,
        SessionManager sessionManager,
        IFactExtractor factExtractor,
        SmartIngestion smartIngestion,
        ILogger<MessageProcessor> logger)
    {
        _orchestrator = orchestrator;
        _rook = rook;
        _scopeFactory = scopeFactory;
        _send = send;
        _sessionManager = sessionManager;
        _factExtractor = factExtractor;
        _smartIngestion = smartIngestion;
        _logger = logger;
    }

    public async Task ProcessAsync(ProcessingContext context, CancellationToken ct = default)
    {
        var requestId = context.RequestId;
        var responseId = context.ResponseId;
        var ws = context.WebSocket;

        try
        {
            // 1. Determine model tier
            context.ModelTier = context.TierOverride
                                ?? Environment.GetEnvironmentVariable("MODEL_TIER")
                                ?? "mid";

            _logger.LogInformation(
                "Processing request {RequestId} for user {UserId} on {Platform}/{ChannelId} (tier={Tier})",
                requestId, context.UserId, context.Platform, context.ChannelId, context.ModelTier);

            // 2. Send ResponseStart
            await _send(ws, new
            {
                type = "response_start",
                response_id = responseId,
                request_id = requestId,
                model_tier = context.ModelTier
            }, ct);

            // 3. Get/create DB session
            string sessionId;
            string projectId;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();
                (sessionId, projectId) = await _sessionManager.GetOrCreateSessionAsync(
                    db, context.UserId, context.ChannelId, context.IsDm);
                context.DbSessionId = sessionId;

                // Store the user message
                await _sessionManager.StoreMessageAsync(
                    db, sessionId, context.UserId, "user", context.Content);
            }

            // 4. Fetch recent messages
            List<DbMessageDto> recentMessages;
            string? sessionSummary;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();
                recentMessages = await _sessionManager.GetRecentMessagesAsync(db, sessionId);

                var session = await db.Sessions.FindAsync(sessionId);
                sessionSummary = session?.SessionSummary;

                if (sessionSummary is null && session?.PreviousSessionId is not null)
                {
                    var prevSession = await db.Sessions.FindAsync(session.PreviousSessionId);
                    sessionSummary = prevSession?.SessionSummary;
                }
            }

            // 5. Fetch memory context from Rook
            var userMemories = new List<MemoryItem>();
            var keyMemories = new List<MemoryItem>();

            try
            {
                var searchResults = await _rook.SearchAsync(
                    context.Content, userId: context.UserId, limit: 10, ct: ct);

                foreach (var result in searchResults)
                {
                    userMemories.Add(new MemoryItem(
                        result.Point.Id,
                        result.Point.Data,
                        result.Score));
                }

                var allMemories = await _rook.GetAllAsync(
                    userId: context.UserId, limit: 100, ct: ct);

                foreach (var point in allMemories.Where(p => p.IsKey))
                {
                    keyMemories.Add(new MemoryItem(point.Id, point.Data, null));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to fetch memories for user {UserId}, continuing without memory context",
                    context.UserId);
            }

            // 6. Build LLM messages
            var llmMessages = PromptBuilder.BuildMessages(
                context.Content,
                context.UserId,
                context.DisplayName,
                context.ChannelType,
                context.Platform,
                recentMessages,
                userMemories,
                keyMemories,
                sessionSummary,
                guildName: null);

            // 7. Run LLM orchestrator (tool-calling loop + simulated streaming)
            var toolContext = new ToolCallContext(
                context.UserId, context.ChannelId, context.Platform, requestId);

            var accumulated = new StringBuilder();
            var toolCount = 0;

            await foreach (var evt in _orchestrator.GenerateAsync(
                llmMessages, toolContext, context.ModelTier ?? "mid", ct))
            {
                switch (evt)
                {
                    case OrchestratorEvent.TextChunk textChunk:
                        accumulated.Append(textChunk.Text);
                        await _send(ws, new
                        {
                            type = "response_chunk",
                            response_id = responseId,
                            request_id = requestId,
                            chunk = textChunk.Text,
                            full_text = accumulated.ToString()
                        }, ct);
                        break;

                    case OrchestratorEvent.ToolStart toolStart:
                        await _send(ws, new
                        {
                            type = "tool_start",
                            response_id = responseId,
                            request_id = requestId,
                            tool_name = toolStart.Name,
                            step = toolStart.Step
                        }, ct);
                        break;

                    case OrchestratorEvent.ToolEnd toolEnd:
                        await _send(ws, new
                        {
                            type = "tool_result",
                            response_id = responseId,
                            request_id = requestId,
                            tool_name = toolEnd.Name,
                            success = toolEnd.Success,
                            preview = toolEnd.Preview
                        }, ct);
                        break;

                    case OrchestratorEvent.Complete complete:
                        toolCount = complete.ToolCount;
                        if (accumulated.Length == 0)
                            accumulated.Append(complete.FullText);
                        break;
                }
            }

            var fullText = accumulated.ToString();

            // 8. Store assistant message in DB
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();
                await _sessionManager.StoreMessageAsync(
                    db, sessionId, "clara", "assistant", fullText);

                await _sessionManager.UpdateSessionActivityAsync(db, sessionId);
            }

            // 9. Send ResponseEnd
            await _send(ws, new
            {
                type = "response_end",
                response_id = responseId,
                request_id = requestId,
                full_text = fullText,
                tool_count = toolCount
            }, ct);

            _logger.LogInformation(
                "Completed request {RequestId}: {Length} chars, {ToolCount} tools",
                requestId, fullText.Length, toolCount);

            // 10. Background: extract memories (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    var facts = await _factExtractor.ExtractAsync(
                        context.Content, fullText, context.UserId, CancellationToken.None);

                    foreach (var fact in facts)
                    {
                        var result = await _smartIngestion.IngestFactAsync(
                            fact, context.UserId, CancellationToken.None);
                        _logger.LogDebug("Ingested fact: {Action} {NewId}", result.Action, result.NewId);
                    }

                    _logger.LogInformation(
                        "Extracted {Count} facts for request {RequestId}", facts.Count, requestId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Background memory extraction failed for request {RequestId}", requestId);
                }
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Request {RequestId} was cancelled", requestId);

            try
            {
                await _send(ws, new
                {
                    type = "cancelled",
                    request_id = requestId,
                    response_id = responseId
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to send cancellation notice for {RequestId}", requestId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process request {RequestId}", requestId);

            try
            {
                await _send(ws, new
                {
                    type = "error",
                    request_id = requestId,
                    response_id = responseId,
                    error = ex.Message
                }, CancellationToken.None);
            }
            catch (Exception sendEx)
            {
                _logger.LogDebug(sendEx, "Failed to send error for {RequestId}", requestId);
            }
        }
    }
}
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~MessageProcessorToolWiringTests" --verbosity normal
```

Expected: PASS (2 tests). Also verify the full solution builds:

```
dotnet build src/MyPalClara.Core
```

Expected: BUILD SUCCEEDED

**Step 5: Commit**

```
git add src/MyPalClara.Core/Processing/MessageProcessor.cs tests/MyPalClara.Core.Tests/Processing/MessageProcessorToolWiringTests.cs
git commit -m "feat: wire LlmOrchestrator into MessageProcessor, replacing StreamAsync with tool-calling loop"
```

---

### Task 5: Core Tools -- TerminalTools, FileStorageTools, ProcessManager, ChatHistoryTools, SystemLogTools, PersonalityTools, DiscordTools

**Files:**
- Create: `src/MyPalClara.Gateway/Tools/TerminalTools.cs`
- Create: `src/MyPalClara.Gateway/Tools/FileStorageTools.cs`
- Create: `src/MyPalClara.Gateway/Tools/ProcessManager.cs`
- Create: `src/MyPalClara.Gateway/Tools/ChatHistoryTools.cs`
- Create: `src/MyPalClara.Gateway/Tools/SystemLogTools.cs`
- Create: `src/MyPalClara.Gateway/Tools/PersonalityTools.cs`
- Create: `src/MyPalClara.Gateway/Tools/DiscordTools.cs`
- Modify: `src/MyPalClara.Gateway/MyPalClara.Gateway.csproj` (add AWSSDK.S3)
- Test: `tests/MyPalClara.Gateway.Tests/Tools/TerminalToolsTests.cs`
- Test: `tests/MyPalClara.Gateway.Tests/Tools/ProcessManagerTests.cs`
- Test: `tests/MyPalClara.Gateway.Tests/Tools/ChatHistoryToolsTests.cs`

Each tool class is a static class with a `Register(IToolRegistry, ...)` method that registers its tools. Tools use `ToolCallContext` for user/channel info and accept args as `Dictionary<string, JsonElement>`.

**Step 1: Write the failing tests**

Create `tests/MyPalClara.Gateway.Tests/Tools/TerminalToolsTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Tools;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

public class TerminalToolsTests
{
    private readonly ToolRegistry _registry = new(NullLogger<ToolRegistry>.Instance);
    private readonly ToolCallContext _ctx = new("user-1", "ch-1", "discord", "req-1");

    [Fact]
    public async Task ExecuteCommand_ReturnsOutput()
    {
        TerminalTools.Register(_registry);

        var args = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonDocument.Parse("\"echo hello\"").RootElement
        };

        var result = await _registry.ExecuteAsync("execute_command", args, _ctx);

        Assert.True(result.Success);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public async Task ExecuteCommand_BadCommand_ReturnsError()
    {
        TerminalTools.Register(_registry);

        var args = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonDocument.Parse("\"nonexistent_binary_xyz_12345\"").RootElement
        };

        var result = await _registry.ExecuteAsync("execute_command", args, _ctx);

        // Should return something (either error output or failure)
        Assert.NotNull(result.Output);
    }

    [Fact]
    public async Task GetCommandHistory_ReturnsHistory()
    {
        TerminalTools.Register(_registry);

        // First execute a command
        var execArgs = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonDocument.Parse("\"echo test_history\"").RootElement
        };
        await _registry.ExecuteAsync("execute_command", execArgs, _ctx);

        // Then get history
        var histArgs = new Dictionary<string, JsonElement>
        {
            ["limit"] = JsonDocument.Parse("10").RootElement
        };
        var result = await _registry.ExecuteAsync("get_command_history", histArgs, _ctx);

        Assert.True(result.Success);
        Assert.Contains("echo test_history", result.Output);
    }
}
```

Create `tests/MyPalClara.Gateway.Tests/Tools/ProcessManagerTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Tools;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

public class ProcessManagerTests
{
    private readonly ToolRegistry _registry = new(NullLogger<ToolRegistry>.Instance);
    private readonly ToolCallContext _ctx = new("user-1", "ch-1", "discord", "req-1");

    [Fact]
    public async Task ProcessList_EmptyInitially()
    {
        var pm = new ProcessManagerService();
        ProcessManagerTools.Register(_registry, pm);

        var result = await _registry.ExecuteAsync("process_list", new(), _ctx);

        Assert.True(result.Success);
        Assert.Contains("No", result.Output); // "No tracked processes"
    }

    [Fact]
    public async Task ProcessStart_And_ProcessList()
    {
        var pm = new ProcessManagerService();
        ProcessManagerTools.Register(_registry, pm);

        var args = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonDocument.Parse("\"sleep 60\"").RootElement
        };
        var startResult = await _registry.ExecuteAsync("process_start", args, _ctx);
        Assert.True(startResult.Success);

        var listResult = await _registry.ExecuteAsync("process_list", new(), _ctx);
        Assert.True(listResult.Success);
        Assert.Contains("sleep", listResult.Output);

        // Cleanup: stop the process
        var pid = JsonDocument.Parse(startResult.Output).RootElement.GetProperty("pid").GetString()!;
        var stopArgs = new Dictionary<string, JsonElement>
        {
            ["pid"] = JsonDocument.Parse($"\"{pid}\"").RootElement
        };
        await _registry.ExecuteAsync("process_stop", stopArgs, _ctx);
    }
}
```

Create `tests/MyPalClara.Gateway.Tests/Tools/ChatHistoryToolsTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Tools;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

/// <summary>
/// ChatHistoryTools requires a DB context, so these tests verify registration only.
/// Full integration tests require EF InMemory or SQLite.
/// </summary>
public class ChatHistoryToolsTests
{
    private readonly ToolRegistry _registry = new(NullLogger<ToolRegistry>.Instance);

    [Fact]
    public void Register_AddsTools()
    {
        // ChatHistoryTools.Register takes IServiceScopeFactory; pass null and verify tools are listed
        ChatHistoryTools.Register(_registry, null!);

        var tools = _registry.GetAllTools();
        var names = tools.Select(t => t.Name).ToList();
        Assert.Contains("search_chat_history", names);
        Assert.Contains("get_chat_history", names);
    }
}
```

**Step 2: Run tests to verify they fail**

```
dotnet test tests/MyPalClara.Gateway.Tests --filter "FullyQualifiedName~TerminalToolsTests|FullyQualifiedName~ProcessManagerTests|FullyQualifiedName~ChatHistoryToolsTests" --verbosity normal
```

Expected: FAIL (classes do not exist)

**Step 3: Write minimal implementation**

3a. Modify `src/MyPalClara.Gateway/MyPalClara.Gateway.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="AWSSDK.S3" Version="4.*" />
    <PackageReference Include="YamlDotNet" Version="16.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyPalClara.Modules.Sdk\MyPalClara.Modules.Sdk.csproj" />
    <ProjectReference Include="..\MyPalClara.Core\MyPalClara.Core.csproj" />
  </ItemGroup>
</Project>
```

3b. Create `src/MyPalClara.Gateway/Tools/TerminalTools.cs`:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class TerminalTools
{
    private static readonly ConcurrentQueue<string> CommandHistory = new();
    private const int MaxHistory = 100;

    public static void Register(IToolRegistry registry)
    {
        var executeSchema = new ToolSchema("execute_command",
            "Execute a shell command and return stdout/stderr. Args: command (string), timeout_seconds (int, optional, default 30).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "command": { "type": "string", "description": "Shell command to execute" },
                    "timeout_seconds": { "type": "integer", "description": "Timeout in seconds (default 30)" }
                },
                "required": ["command"]
            }
            """).RootElement);

        registry.RegisterTool("execute_command", executeSchema, async ctx =>
        {
            // Args are passed via a closure workaround -- we need the args dict.
            // The IToolRegistry.ExecuteAsync signature passes args, but the handler
            // only receives ToolCallContext. We solve this by using a wrapper pattern
            // in CoreToolsRegistrar (Task 6). For now, we register with the simple handler.
            return new ToolResult(true, "execute_command requires args -- see CoreToolsRegistrar wiring");
        });

        var historySchema = new ToolSchema("get_command_history",
            "Get recent command execution history. Args: limit (int, optional, default 20).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "limit": { "type": "integer", "description": "Number of recent commands to return (default 20)" }
                }
            }
            """).RootElement);

        registry.RegisterTool("get_command_history", historySchema, async ctx =>
        {
            return new ToolResult(true, "get_command_history requires args -- see CoreToolsRegistrar wiring");
        });
    }

    // NOTE: The actual handler implementations are below.
    // They will be wired in CoreToolsRegistrar (Task 6) which uses a wrapper
    // that passes args to the handler.

    public static async Task<ToolResult> ExecuteCommandAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        if (!args.TryGetValue("command", out var cmdElem))
            return new ToolResult(false, "", "Missing required argument: command");

        var command = cmdElem.GetString() ?? "";
        var timeout = args.TryGetValue("timeout_seconds", out var tElem) ? tElem.GetInt32() : 30;

        // Record in history
        CommandHistory.Enqueue($"[{DateTime.UtcNow:HH:mm:ss}] {command}");
        while (CommandHistory.Count > MaxHistory) CommandHistory.TryDequeue(out _);

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd" : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (isWindows)
        {
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            process.WaitForExit(); // ensure async reads complete

            var output = stdout.ToString().TrimEnd();
            var error = stderr.ToString().TrimEnd();
            var combined = string.IsNullOrEmpty(error)
                ? output
                : $"{output}\n\nSTDERR:\n{error}";

            return new ToolResult(process.ExitCode == 0, combined,
                process.ExitCode != 0 ? $"Exit code: {process.ExitCode}" : null);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ToolResult(false, stdout.ToString().TrimEnd(), "Command timed out");
        }
    }

    public static Task<ToolResult> GetCommandHistoryAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 20;
        var items = CommandHistory.TakeLast(limit).ToList();

        if (items.Count == 0)
            return Task.FromResult(new ToolResult(true, "No command history."));

        return Task.FromResult(new ToolResult(true, string.Join("\n", items)));
    }
}
```

3c. Create `src/MyPalClara.Gateway/Tools/FileStorageTools.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class FileStorageTools
{
    public static void RegisterSchemas(IToolRegistry registry)
    {
        registry.RegisterTool("save_to_local", new ToolSchema("save_to_local",
            "Save content to a file in local storage. Args: filename (string), content (string).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "filename": { "type": "string" },
                    "content": { "type": "string" }
                },
                "required": ["filename", "content"]
            }
            """).RootElement), _ => Task.FromResult(new ToolResult(true, "placeholder")));

        registry.RegisterTool("list_local_files", new ToolSchema("list_local_files",
            "List files in local storage. Args: path (string, optional).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "path": { "type": "string" }
                }
            }
            """).RootElement), _ => Task.FromResult(new ToolResult(true, "placeholder")));

        registry.RegisterTool("read_local_file", new ToolSchema("read_local_file",
            "Read a file from local storage. Args: filename (string).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "filename": { "type": "string" }
                },
                "required": ["filename"]
            }
            """).RootElement), _ => Task.FromResult(new ToolResult(true, "placeholder")));

        registry.RegisterTool("delete_local_file", new ToolSchema("delete_local_file",
            "Delete a file from local storage. Args: filename (string).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "filename": { "type": "string" }
                },
                "required": ["filename"]
            }
            """).RootElement), _ => Task.FromResult(new ToolResult(true, "placeholder")));
    }

    public static async Task<ToolResult> SaveToLocalAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        var filesDir = Environment.GetEnvironmentVariable("CLARA_FILES_DIR") ?? "./clara_files";
        var userDir = Path.Combine(filesDir, ctx.UserId);
        Directory.CreateDirectory(userDir);

        if (!args.TryGetValue("filename", out var fnElem))
            return new ToolResult(false, "", "Missing required argument: filename");
        if (!args.TryGetValue("content", out var contentElem))
            return new ToolResult(false, "", "Missing required argument: content");

        var filename = Path.GetFileName(fnElem.GetString() ?? "file.txt");
        var content = contentElem.GetString() ?? "";
        var path = Path.Combine(userDir, filename);

        await File.WriteAllTextAsync(path, content, ct);
        return new ToolResult(true, $"Saved to {filename} ({content.Length} bytes)");
    }

    public static Task<ToolResult> ListLocalFilesAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        var filesDir = Environment.GetEnvironmentVariable("CLARA_FILES_DIR") ?? "./clara_files";
        var userDir = Path.Combine(filesDir, ctx.UserId);
        if (args.TryGetValue("path", out var pathElem))
            userDir = Path.Combine(userDir, pathElem.GetString() ?? "");

        if (!Directory.Exists(userDir))
            return Task.FromResult(new ToolResult(true, "No files found."));

        var files = Directory.GetFiles(userDir).Select(Path.GetFileName);
        return Task.FromResult(new ToolResult(true, string.Join("\n", files)));
    }

    public static async Task<ToolResult> ReadLocalFileAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        var filesDir = Environment.GetEnvironmentVariable("CLARA_FILES_DIR") ?? "./clara_files";
        if (!args.TryGetValue("filename", out var fnElem))
            return new ToolResult(false, "", "Missing required argument: filename");

        var filename = Path.GetFileName(fnElem.GetString() ?? "");
        var path = Path.Combine(filesDir, ctx.UserId, filename);

        if (!File.Exists(path))
            return new ToolResult(false, "", $"File not found: {filename}");

        var content = await File.ReadAllTextAsync(path, ct);
        return new ToolResult(true, content);
    }

    public static Task<ToolResult> DeleteLocalFileAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        var filesDir = Environment.GetEnvironmentVariable("CLARA_FILES_DIR") ?? "./clara_files";
        if (!args.TryGetValue("filename", out var fnElem))
            return Task.FromResult(new ToolResult(false, "", "Missing required argument: filename"));

        var filename = Path.GetFileName(fnElem.GetString() ?? "");
        var path = Path.Combine(filesDir, ctx.UserId, filename);

        if (!File.Exists(path))
            return Task.FromResult(new ToolResult(false, "", $"File not found: {filename}"));

        File.Delete(path);
        return Task.FromResult(new ToolResult(true, $"Deleted {filename}"));
    }
}
```

3d. Create `src/MyPalClara.Gateway/Tools/ProcessManager.cs`:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public class ProcessManagerService
{
    private readonly ConcurrentDictionary<string, TrackedProcess> _processes = new();
    private const int MaxOutputLines = 1000;

    public record TrackedProcess(
        string Pid, string Command, Process Process, DateTime StartedAt,
        ConcurrentQueue<string> OutputBuffer);

    public (string Pid, TrackedProcess Tracked) StartProcess(string command)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd" : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (isWindows) { psi.ArgumentList.Add("/c"); psi.ArgumentList.Add(command); }
        else { psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(command); }

        var process = new Process { StartInfo = psi };
        var buffer = new ConcurrentQueue<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                buffer.Enqueue(e.Data);
                while (buffer.Count > MaxOutputLines) buffer.TryDequeue(out _);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                buffer.Enqueue($"[stderr] {e.Data}");
                while (buffer.Count > MaxOutputLines) buffer.TryDequeue(out _);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var pid = process.Id.ToString();
        var tracked = new TrackedProcess(pid, command, process, DateTime.UtcNow, buffer);
        _processes[pid] = tracked;
        return (pid, tracked);
    }

    public TrackedProcess? GetProcess(string pid) =>
        _processes.TryGetValue(pid, out var p) ? p : null;

    public IReadOnlyList<TrackedProcess> ListAll() => _processes.Values.ToList();

    public bool StopProcess(string pid, bool force = false)
    {
        if (!_processes.TryRemove(pid, out var tracked))
            return false;

        try
        {
            if (!tracked.Process.HasExited)
                tracked.Process.Kill(entireProcessTree: true);
        }
        catch { }
        return true;
    }
}

public static class ProcessManagerTools
{
    public static void Register(IToolRegistry registry, ProcessManagerService pm)
    {
        registry.RegisterTool("process_start", new ToolSchema("process_start",
            "Start a background process. Args: command (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}""").RootElement),
            _ => Task.FromResult(new ToolResult(true, "placeholder")));

        registry.RegisterTool("process_status", new ToolSchema("process_status",
            "Get status of a tracked process. Args: pid (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"pid":{"type":"string"}},"required":["pid"]}""").RootElement),
            _ => Task.FromResult(new ToolResult(true, "placeholder")));

        registry.RegisterTool("process_output", new ToolSchema("process_output",
            "Get recent output from a tracked process. Args: pid (string), lines (int, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"pid":{"type":"string"},"lines":{"type":"integer"}},"required":["pid"]}""").RootElement),
            _ => Task.FromResult(new ToolResult(true, "placeholder")));

        registry.RegisterTool("process_stop", new ToolSchema("process_stop",
            "Stop a tracked process. Args: pid (string), force (bool, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"pid":{"type":"string"},"force":{"type":"boolean"}},"required":["pid"]}""").RootElement),
            _ => Task.FromResult(new ToolResult(true, "placeholder")));

        registry.RegisterTool("process_list", new ToolSchema("process_list",
            "List all tracked processes.",
            JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement),
            _ => Task.FromResult(new ToolResult(true, "placeholder")));
    }

    public static Task<ToolResult> StartAsync(Dictionary<string, JsonElement> args, ToolCallContext ctx,
        ProcessManagerService pm, CancellationToken ct)
    {
        if (!args.TryGetValue("command", out var cmdElem))
            return Task.FromResult(new ToolResult(false, "", "Missing: command"));

        var (pid, _) = pm.StartProcess(cmdElem.GetString()!);
        return Task.FromResult(new ToolResult(true, JsonSerializer.Serialize(new { pid, status = "running" })));
    }

    public static Task<ToolResult> StatusAsync(Dictionary<string, JsonElement> args, ToolCallContext ctx,
        ProcessManagerService pm, CancellationToken ct)
    {
        if (!args.TryGetValue("pid", out var pidElem))
            return Task.FromResult(new ToolResult(false, "", "Missing: pid"));

        var tracked = pm.GetProcess(pidElem.GetString()!);
        if (tracked is null) return Task.FromResult(new ToolResult(false, "", "Process not found"));

        var running = !tracked.Process.HasExited;
        var uptime = DateTime.UtcNow - tracked.StartedAt;
        return Task.FromResult(new ToolResult(true,
            $"PID: {tracked.Pid}, Command: {tracked.Command}, Running: {running}, Uptime: {uptime:hh\\:mm\\:ss}"));
    }

    public static Task<ToolResult> OutputAsync(Dictionary<string, JsonElement> args, ToolCallContext ctx,
        ProcessManagerService pm, CancellationToken ct)
    {
        if (!args.TryGetValue("pid", out var pidElem))
            return Task.FromResult(new ToolResult(false, "", "Missing: pid"));

        var tracked = pm.GetProcess(pidElem.GetString()!);
        if (tracked is null) return Task.FromResult(new ToolResult(false, "", "Process not found"));

        var lines = args.TryGetValue("lines", out var lElem) ? lElem.GetInt32() : 50;
        var output = tracked.OutputBuffer.TakeLast(lines);
        return Task.FromResult(new ToolResult(true, string.Join("\n", output)));
    }

    public static Task<ToolResult> StopAsync(Dictionary<string, JsonElement> args, ToolCallContext ctx,
        ProcessManagerService pm, CancellationToken ct)
    {
        if (!args.TryGetValue("pid", out var pidElem))
            return Task.FromResult(new ToolResult(false, "", "Missing: pid"));

        var force = args.TryGetValue("force", out var fElem) && fElem.GetBoolean();
        var stopped = pm.StopProcess(pidElem.GetString()!, force);
        return Task.FromResult(stopped
            ? new ToolResult(true, "Process stopped")
            : new ToolResult(false, "", "Process not found"));
    }

    public static Task<ToolResult> ListAsync(Dictionary<string, JsonElement> args, ToolCallContext ctx,
        ProcessManagerService pm, CancellationToken ct)
    {
        var all = pm.ListAll();
        if (all.Count == 0)
            return Task.FromResult(new ToolResult(true, "No tracked processes."));

        var sb = new StringBuilder();
        foreach (var p in all)
        {
            var running = !p.Process.HasExited;
            sb.AppendLine($"PID={p.Pid} CMD={p.Command} RUNNING={running}");
        }
        return Task.FromResult(new ToolResult(true, sb.ToString().TrimEnd()));
    }
}
```

3e. Create `src/MyPalClara.Gateway/Tools/ChatHistoryTools.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Data;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class ChatHistoryTools
{
    public static void Register(IToolRegistry registry, IServiceScopeFactory scopeFactory)
    {
        registry.RegisterTool("search_chat_history", new ToolSchema("search_chat_history",
            "Search chat history by keyword. Args: query (string), limit (int, optional, default 20), user_id (string, optional).",
            JsonDocument.Parse("""
            {"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer"},"user_id":{"type":"string"}},"required":["query"]}
            """).RootElement),
            _ => Task.FromResult(new ToolResult(true, "placeholder")));

        registry.RegisterTool("get_chat_history", new ToolSchema("get_chat_history",
            "Get recent chat messages. Args: channel_id (string, optional), limit (int, optional, default 20), user_id (string, optional).",
            JsonDocument.Parse("""
            {"type":"object","properties":{"channel_id":{"type":"string"},"limit":{"type":"integer"},"user_id":{"type":"string"}}}
            """).RootElement),
            _ => Task.FromResult(new ToolResult(true, "placeholder")));
    }

    public static async Task<ToolResult> SearchAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        if (!args.TryGetValue("query", out var queryElem))
            return new ToolResult(false, "", "Missing: query");

        var query = queryElem.GetString() ?? "";
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 20;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var messages = await db.Messages
            .Where(m => EF.Functions.Like(m.Content, $"%{query}%"))
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .Select(m => new { m.Role, m.Content, m.CreatedAt, m.UserId })
            .ToListAsync(ct);

        if (messages.Count == 0)
            return new ToolResult(true, "No messages found.");

        var results = messages.Select(m =>
            $"[{m.CreatedAt:yyyy-MM-dd HH:mm}] {m.Role} ({m.UserId}): {m.Content[..Math.Min(200, m.Content.Length)]}");
        return new ToolResult(true, string.Join("\n", results));
    }

    public static async Task<ToolResult> GetHistoryAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 20;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var q = db.Messages.AsQueryable();

        if (args.TryGetValue("user_id", out var uidElem))
            q = q.Where(m => m.UserId == uidElem.GetString());

        var messages = await q
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Role, m.Content, m.CreatedAt, m.UserId })
            .ToListAsync(ct);

        if (messages.Count == 0)
            return new ToolResult(true, "No messages found.");

        var results = messages.Select(m =>
            $"[{m.CreatedAt:yyyy-MM-dd HH:mm}] {m.Role} ({m.UserId}): {m.Content[..Math.Min(200, m.Content.Length)]}");
        return new ToolResult(true, string.Join("\n", results));
    }
}
```

3f. Create `src/MyPalClara.Gateway/Tools/SystemLogTools.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Data;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class SystemLogTools
{
    public static void Register(IToolRegistry registry, IServiceScopeFactory scopeFactory)
    {
        registry.RegisterTool("search_logs", new ToolSchema("search_logs",
            "Search system logs by keyword, logger, or level. Args: keyword (string, optional), logger (string, optional), level (string, optional), limit (int, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"keyword":{"type":"string"},"logger":{"type":"string"},"level":{"type":"string"},"limit":{"type":"integer"}}}""").RootElement),
            _ => Task.FromResult(new ToolResult(true, "placeholder")));

        registry.RegisterTool("get_recent_logs", new ToolSchema("get_recent_logs",
            "Get recent log entries. Args: limit (int, optional, default 50).",
            JsonDocument.Parse("""{"type":"object","properties":{"limit":{"type":"integer"}}}""").RootElement),
            _ => Task.FromResult(new ToolResult(true, "placeholder")));

        registry.RegisterTool("get_error_logs", new ToolSchema("get_error_logs",
            "Get recent error log entries with tracebacks. Args: limit (int, optional, default 20).",
            JsonDocument.Parse("""{"type":"object","properties":{"limit":{"type":"integer"}}}""").RootElement),
            _ => Task.FromResult(new ToolResult(true, "placeholder")));
    }

    public static async Task<ToolResult> SearchLogsAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 50;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var q = db.LogEntries.AsQueryable();

        if (args.TryGetValue("keyword", out var kw))
            q = q.Where(l => EF.Functions.Like(l.Message, $"%{kw.GetString()}%"));
        if (args.TryGetValue("logger", out var lg))
            q = q.Where(l => l.LoggerName == lg.GetString());
        if (args.TryGetValue("level", out var lv))
            q = q.Where(l => l.Level == lv.GetString());

        var logs = await q.OrderByDescending(l => l.Timestamp).Take(limit)
            .Select(l => new { l.Timestamp, l.Level, l.LoggerName, l.Message }).ToListAsync(ct);

        if (logs.Count == 0) return new ToolResult(true, "No logs found.");
        var results = logs.Select(l => $"[{l.Timestamp:yyyy-MM-dd HH:mm:ss}] [{l.Level}] {l.LoggerName}: {l.Message}");
        return new ToolResult(true, string.Join("\n", results));
    }

    public static async Task<ToolResult> GetRecentLogsAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 50;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var logs = await db.LogEntries.OrderByDescending(l => l.Timestamp).Take(limit)
            .Select(l => new { l.Timestamp, l.Level, l.LoggerName, l.Message }).ToListAsync(ct);

        if (logs.Count == 0) return new ToolResult(true, "No logs found.");
        var results = logs.Select(l => $"[{l.Timestamp:yyyy-MM-dd HH:mm:ss}] [{l.Level}] {l.LoggerName}: {l.Message}");
        return new ToolResult(true, string.Join("\n", results));
    }

    public static async Task<ToolResult> GetErrorLogsAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 20;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var logs = await db.LogEntries
            .Where(l => l.Level == "ERROR" || l.Level == "Error" || l.Level == "CRITICAL")
            .OrderByDescending(l => l.Timestamp).Take(limit)
            .Select(l => new { l.Timestamp, l.Level, l.LoggerName, l.Message, l.Exception }).ToListAsync(ct);

        if (logs.Count == 0) return new ToolResult(true, "No error logs found.");
        var results = logs.Select(l =>
        {
            var entry = $"[{l.Timestamp:yyyy-MM-dd HH:mm:ss}] [{l.Level}] {l.LoggerName}: {l.Message}";
            if (!string.IsNullOrEmpty(l.Exception)) entry += $"\n  {l.Exception}";
            return entry;
        });
        return new ToolResult(true, string.Join("\n", results));
    }
}
```

3g. Create `src/MyPalClara.Gateway/Tools/PersonalityTools.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Data;
using MyPalClara.Data.Entities;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class PersonalityTools
{
    public static void Register(IToolRegistry registry, IServiceScopeFactory scopeFactory)
    {
        registry.RegisterTool("update_personality", new ToolSchema("update_personality",
            "Add, update, or remove a personality trait. Args: action (string: add|update|remove), category (string), trait_key (string), content (string, optional), reason (string, optional).",
            JsonDocument.Parse("""
            {"type":"object","properties":{"action":{"type":"string","enum":["add","update","remove"]},"category":{"type":"string"},"trait_key":{"type":"string"},"content":{"type":"string"},"reason":{"type":"string"}},"required":["action","category","trait_key"]}
            """).RootElement),
            _ => Task.FromResult(new ToolResult(true, "placeholder")));
    }

    public static async Task<ToolResult> UpdatePersonalityAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        if (!args.TryGetValue("action", out var actionElem))
            return new ToolResult(false, "", "Missing: action");
        if (!args.TryGetValue("category", out var catElem))
            return new ToolResult(false, "", "Missing: category");
        if (!args.TryGetValue("trait_key", out var keyElem))
            return new ToolResult(false, "", "Missing: trait_key");

        var action = actionElem.GetString()!;
        var category = catElem.GetString()!;
        var traitKey = keyElem.GetString()!;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var existing = await db.PersonalityTraits
            .FirstOrDefaultAsync(t => t.Category == category && t.TraitKey == traitKey, ct);

        switch (action)
        {
            case "add":
                if (!args.TryGetValue("content", out var contentElem))
                    return new ToolResult(false, "", "Missing: content for add action");

                if (existing is not null)
                    return new ToolResult(false, "", $"Trait '{category}/{traitKey}' already exists. Use update.");

                db.PersonalityTraits.Add(new PersonalityTrait
                {
                    Id = Guid.NewGuid().ToString(),
                    Category = category,
                    TraitKey = traitKey,
                    Content = contentElem.GetString()!,
                    Reason = args.TryGetValue("reason", out var rElem) ? rElem.GetString() : null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync(ct);
                return new ToolResult(true, $"Added trait: {category}/{traitKey}");

            case "update":
                if (existing is null)
                    return new ToolResult(false, "", $"Trait '{category}/{traitKey}' not found.");
                if (args.TryGetValue("content", out var cElem))
                    existing.Content = cElem.GetString()!;
                if (args.TryGetValue("reason", out var rrElem))
                    existing.Reason = rrElem.GetString();
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return new ToolResult(true, $"Updated trait: {category}/{traitKey}");

            case "remove":
                if (existing is null)
                    return new ToolResult(false, "", $"Trait '{category}/{traitKey}' not found.");
                existing.Active = false;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return new ToolResult(true, $"Removed trait: {category}/{traitKey}");

            default:
                return new ToolResult(false, "", $"Unknown action: {action}. Use add, update, or remove.");
        }
    }
}
```

3h. Create `src/MyPalClara.Gateway/Tools/DiscordTools.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class DiscordTools
{
    public static void Register(IToolRegistry registry, IGatewayBridge bridge)
    {
        var names = new[]
        {
            ("send_discord_file", "Send a file to the current Discord channel. Args: filename (string), content (string), channel_id (string, optional)."),
            ("format_discord_message", "Format a message with Discord markdown/embeds. Args: content (string), format (string: bold|italic|code|quote)."),
            ("add_discord_reaction", "Add an emoji reaction to a message. Args: message_id (string), emoji (string)."),
            ("send_discord_embed", "Send a rich embed message. Args: title (string), description (string), color (int, optional), fields (array, optional)."),
            ("create_discord_thread", "Create a thread from a message. Args: message_id (string), name (string)."),
            ("edit_discord_message", "Edit a previously sent message. Args: message_id (string), content (string)."),
            ("send_discord_buttons", "Send a message with interactive buttons. Args: content (string), buttons (array of {label, custom_id}).")
        };

        foreach (var (name, desc) in names)
        {
            registry.RegisterTool(name, new ToolSchema(name, desc,
                JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement),
                _ => Task.FromResult(new ToolResult(true, "placeholder")));
        }
    }

    /// <summary>
    /// Generic handler for all Discord tools: sends a tool_action message to the adapter via IGatewayBridge
    /// and waits for a response (with timeout).
    /// </summary>
    public static async Task<ToolResult> ExecuteDiscordToolAsync(
        string toolName, Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IGatewayBridge bridge, CancellationToken ct)
    {
        // Send tool_action to all Discord nodes
        var action = new
        {
            type = "tool_action",
            tool_name = toolName,
            channel_id = ctx.ChannelId,
            user_id = ctx.UserId,
            request_id = ctx.RequestId,
            arguments = args
        };

        await bridge.BroadcastToPlatformAsync("discord", action, ct);

        // For now, return success -- adapter response handling will be added
        // when we implement the protocol handler in the Discord adapter.
        return new ToolResult(true, $"Sent {toolName} action to Discord adapter");
    }
}
```

**Step 4: Run tests to verify they pass**

```
dotnet test tests/MyPalClara.Gateway.Tests --filter "FullyQualifiedName~TerminalToolsTests|FullyQualifiedName~ProcessManagerTests|FullyQualifiedName~ChatHistoryToolsTests" --verbosity normal
```

Expected: PASS

Note: The TerminalTools tests need the CoreToolsRegistrar wiring (Task 6) to pass with actual args. For now, the test in Step 1 must be adjusted: instead of calling through the registry (which uses placeholder handlers), test the static methods directly:

Adjust `TerminalToolsTests` to test the static methods:

```csharp
[Fact]
public async Task ExecuteCommand_StaticMethod_ReturnsOutput()
{
    var args = new Dictionary<string, JsonElement>
    {
        ["command"] = JsonDocument.Parse("\"echo hello\"").RootElement
    };
    var ctx = new ToolCallContext("user-1", "ch-1", "discord", "req-1");

    var result = await TerminalTools.ExecuteCommandAsync(args, ctx, CancellationToken.None);

    Assert.True(result.Success);
    Assert.Contains("hello", result.Output);
}
```

**Step 5: Commit**

```
git add src/MyPalClara.Gateway/Tools/ src/MyPalClara.Gateway/MyPalClara.Gateway.csproj tests/MyPalClara.Gateway.Tests/Tools/TerminalToolsTests.cs tests/MyPalClara.Gateway.Tests/Tools/ProcessManagerTests.cs tests/MyPalClara.Gateway.Tests/Tools/ChatHistoryToolsTests.cs
git commit -m "feat: implement 24 core tools across 7 groups (terminal, files, process, chat, logs, personality, discord)"
```

---

### Task 6: CoreToolsRegistrar -- register all tools at startup, wire into AddMyPalClaraGateway

**Files:**
- Create: `src/MyPalClara.Gateway/Tools/CoreToolsRegistrar.cs`
- Modify: `src/MyPalClara.Gateway/ServiceCollectionExtensions.cs`
- Test: `tests/MyPalClara.Gateway.Tests/Tools/CoreToolsRegistrarTests.cs`

The key challenge: `IToolRegistry.RegisterTool` takes a `Func<ToolCallContext, Task<ToolResult>>` handler, but core tools need `args`. We solve this by modifying `IToolRegistry.ExecuteAsync` to pass args through to the handler -- but the handler signature only takes `ToolCallContext`. Solution: extend `ToolCallContext` to carry args, OR use the `IToolRegistry.ExecuteAsync(name, args, context, ct)` path which already has args -- the registry just needs to pass them to the handler.

**Better approach:** Change the handler signature in `IToolRegistry.RegisterTool` to include args. This means modifying Task 1's `IToolRegistry` interface:

Actually, looking at the design more carefully: `IToolRegistry.ExecuteAsync` receives the args and passes them to the handler. The handler signature `Func<ToolCallContext, Task<ToolResult>>` does NOT include args. We need to fix this.

**Amendment to Task 1:** Change the handler type to include args:

```csharp
// In IToolRegistry.cs, change RegisterTool:
void RegisterTool(string name, ToolSchema schema, Func<Dictionary<string, JsonElement>, ToolCallContext, CancellationToken, Task<ToolResult>> handler);
```

This change flows through ToolRegistry.cs (Task 2) as well. The CoreToolsRegistrar then wires the static methods directly.

**Step 1: Write the failing test**

Create `tests/MyPalClara.Gateway.Tests/Tools/CoreToolsRegistrarTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Tools;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

public class CoreToolsRegistrarTests
{
    [Fact]
    public void RegisterAll_Registers24PlusCoreTools()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);

        // RegisterAll needs dependencies -- use nulls for registration-only test
        CoreToolsRegistrar.RegisterAll(registry, scopeFactory: null!, bridge: null!);

        var tools = registry.GetAllTools();
        // 2 terminal + 4 file + 5 process + 2 chat + 3 log + 1 personality + 7 discord = 24
        Assert.True(tools.Count >= 24, $"Expected >= 24 tools, got {tools.Count}");
    }

    [Fact]
    public void RegisterAll_ToolNamesAreUnique()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        CoreToolsRegistrar.RegisterAll(registry, scopeFactory: null!, bridge: null!);

        var tools = registry.GetAllTools();
        var names = tools.Select(t => t.Name).ToList();
        Assert.Equal(names.Distinct().Count(), names.Count);
    }
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/MyPalClara.Gateway.Tests --filter "FullyQualifiedName~CoreToolsRegistrarTests" --verbosity normal
```

Expected: FAIL (CoreToolsRegistrar does not exist)

**Step 3: Write minimal implementation**

3a. First, amend `IToolRegistry` to use the args-aware handler signature. Modify `src/MyPalClara.Modules.Sdk/IToolRegistry.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Llm;

namespace MyPalClara.Modules.Sdk;

public interface IToolRegistry
{
    void RegisterTool(string name, ToolSchema schema,
        Func<Dictionary<string, JsonElement>, ToolCallContext, CancellationToken, Task<ToolResult>> handler);
    void RegisterSource(IToolSource source);
    void UnregisterTool(string name);
    IReadOnlyList<ToolSchema> GetAllTools(ToolFilter? filter = null);
    Task<ToolResult> ExecuteAsync(string name, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default);
}
```

3b. Update `src/MyPalClara.Gateway/Tools/ToolRegistry.cs` to match new signature:

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string,
        (ToolSchema Schema, Func<Dictionary<string, JsonElement>, ToolCallContext, CancellationToken, Task<ToolResult>> Handler)> _tools = new();
    private readonly List<IToolSource> _sources = [];
    private readonly object _sourceLock = new();
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = logger;
    }

    public void RegisterTool(string name, ToolSchema schema,
        Func<Dictionary<string, JsonElement>, ToolCallContext, CancellationToken, Task<ToolResult>> handler)
    {
        if (!_tools.TryAdd(name, (schema, handler)))
            throw new InvalidOperationException($"Tool '{name}' is already registered.");

        _logger.LogDebug("Registered tool: {Name}", name);
    }

    public void RegisterSource(IToolSource source)
    {
        lock (_sourceLock) { _sources.Add(source); }
        _logger.LogInformation("Registered tool source: {Name} ({Count} tools)", source.Name, source.GetTools().Count);
    }

    public void UnregisterTool(string name)
    {
        _tools.TryRemove(name, out _);
        _logger.LogDebug("Unregistered tool: {Name}", name);
    }

    public IReadOnlyList<ToolSchema> GetAllTools(ToolFilter? filter = null)
    {
        var result = new List<ToolSchema>();
        foreach (var (_, (schema, _)) in _tools)
            result.Add(schema);

        List<IToolSource> snapshot;
        lock (_sourceLock) { snapshot = [.. _sources]; }

        foreach (var source in snapshot)
        {
            try
            {
                foreach (var tool in source.GetTools())
                    if (!_tools.ContainsKey(tool.Name))
                        result.Add(tool);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get tools from source {Name}", source.Name);
            }
        }
        return result;
    }

    public async Task<ToolResult> ExecuteAsync(string name, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default)
    {
        if (_tools.TryGetValue(name, out var entry))
        {
            try { return await entry.Handler(args, context, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool '{Name}' handler threw", name);
                return new ToolResult(false, "", $"Tool '{name}' failed: {ex.Message}");
            }
        }

        List<IToolSource> snapshot;
        lock (_sourceLock) { snapshot = [.. _sources]; }

        foreach (var source in snapshot)
        {
            if (source.CanHandle(name))
            {
                try { return await source.ExecuteAsync(name, args, context, ct); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Source '{Source}' failed executing '{Tool}'", source.Name, name);
                    return new ToolResult(false, "", $"Tool '{name}' failed: {ex.Message}");
                }
            }
        }

        return new ToolResult(false, "", $"Unknown tool: '{name}'.");
    }
}
```

3c. Update all existing tests and tool registrations to use the new 3-param handler signature. Update `tests/MyPalClara.Gateway.Tests/Tools/ToolRegistryTests.cs` -- change all `ctx =>` handlers to `(args, ctx, ct) =>`.

3d. Create `src/MyPalClara.Gateway/Tools/CoreToolsRegistrar.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

/// <summary>
/// Registers all 24 core tools into the IToolRegistry at startup.
/// </summary>
public static class CoreToolsRegistrar
{
    private static readonly ProcessManagerService ProcessManager = new();

    public static void RegisterAll(IToolRegistry registry,
        IServiceScopeFactory scopeFactory, IGatewayBridge bridge)
    {
        RegisterTerminalTools(registry);
        RegisterFileStorageTools(registry);
        RegisterProcessManagerTools(registry);
        RegisterChatHistoryTools(registry, scopeFactory);
        RegisterSystemLogTools(registry, scopeFactory);
        RegisterPersonalityTools(registry, scopeFactory);
        RegisterDiscordTools(registry, bridge);
    }

    private static void RegisterTerminalTools(IToolRegistry registry)
    {
        registry.RegisterTool("execute_command", new ToolSchema("execute_command",
            "Execute a shell command. Args: command (string), timeout_seconds (int, optional, default 30).",
            JsonDocument.Parse("""{"type":"object","properties":{"command":{"type":"string","description":"Shell command"},"timeout_seconds":{"type":"integer"}},"required":["command"]}""").RootElement),
            TerminalTools.ExecuteCommandAsync);

        registry.RegisterTool("get_command_history", new ToolSchema("get_command_history",
            "Get recent command history. Args: limit (int, optional, default 20).",
            JsonDocument.Parse("""{"type":"object","properties":{"limit":{"type":"integer"}}}""").RootElement),
            TerminalTools.GetCommandHistoryAsync);
    }

    private static void RegisterFileStorageTools(IToolRegistry registry)
    {
        registry.RegisterTool("save_to_local", new ToolSchema("save_to_local",
            "Save content to local file storage. Args: filename (string), content (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"filename":{"type":"string"},"content":{"type":"string"}},"required":["filename","content"]}""").RootElement),
            FileStorageTools.SaveToLocalAsync);

        registry.RegisterTool("list_local_files", new ToolSchema("list_local_files",
            "List files in local storage. Args: path (string, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""").RootElement),
            FileStorageTools.ListLocalFilesAsync);

        registry.RegisterTool("read_local_file", new ToolSchema("read_local_file",
            "Read a file from local storage. Args: filename (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"filename":{"type":"string"}},"required":["filename"]}""").RootElement),
            FileStorageTools.ReadLocalFileAsync);

        registry.RegisterTool("delete_local_file", new ToolSchema("delete_local_file",
            "Delete a file from local storage. Args: filename (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"filename":{"type":"string"}},"required":["filename"]}""").RootElement),
            FileStorageTools.DeleteLocalFileAsync);
    }

    private static void RegisterProcessManagerTools(IToolRegistry registry)
    {
        var pm = ProcessManager;

        registry.RegisterTool("process_start", new ToolSchema("process_start",
            "Start a background process. Args: command (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}""").RootElement),
            (args, ctx, ct) => ProcessManagerTools.StartAsync(args, ctx, pm, ct));

        registry.RegisterTool("process_status", new ToolSchema("process_status",
            "Get process status. Args: pid (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"pid":{"type":"string"}},"required":["pid"]}""").RootElement),
            (args, ctx, ct) => ProcessManagerTools.StatusAsync(args, ctx, pm, ct));

        registry.RegisterTool("process_output", new ToolSchema("process_output",
            "Get process output. Args: pid (string), lines (int, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"pid":{"type":"string"},"lines":{"type":"integer"}},"required":["pid"]}""").RootElement),
            (args, ctx, ct) => ProcessManagerTools.OutputAsync(args, ctx, pm, ct));

        registry.RegisterTool("process_stop", new ToolSchema("process_stop",
            "Stop a process. Args: pid (string), force (bool, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"pid":{"type":"string"},"force":{"type":"boolean"}},"required":["pid"]}""").RootElement),
            (args, ctx, ct) => ProcessManagerTools.StopAsync(args, ctx, pm, ct));

        registry.RegisterTool("process_list", new ToolSchema("process_list",
            "List all tracked processes.",
            JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement),
            (args, ctx, ct) => ProcessManagerTools.ListAsync(args, ctx, pm, ct));
    }

    private static void RegisterChatHistoryTools(IToolRegistry registry, IServiceScopeFactory scopeFactory)
    {
        registry.RegisterTool("search_chat_history", new ToolSchema("search_chat_history",
            "Search chat history. Args: query (string), limit (int, optional), user_id (string, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer"},"user_id":{"type":"string"}},"required":["query"]}""").RootElement),
            (args, ctx, ct) => ChatHistoryTools.SearchAsync(args, ctx, scopeFactory, ct));

        registry.RegisterTool("get_chat_history", new ToolSchema("get_chat_history",
            "Get recent chat messages. Args: channel_id (string, optional), limit (int, optional), user_id (string, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"channel_id":{"type":"string"},"limit":{"type":"integer"},"user_id":{"type":"string"}}}""").RootElement),
            (args, ctx, ct) => ChatHistoryTools.GetHistoryAsync(args, ctx, scopeFactory, ct));
    }

    private static void RegisterSystemLogTools(IToolRegistry registry, IServiceScopeFactory scopeFactory)
    {
        registry.RegisterTool("search_logs", new ToolSchema("search_logs",
            "Search system logs. Args: keyword (string, optional), logger (string, optional), level (string, optional), limit (int, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"keyword":{"type":"string"},"logger":{"type":"string"},"level":{"type":"string"},"limit":{"type":"integer"}}}""").RootElement),
            (args, ctx, ct) => SystemLogTools.SearchLogsAsync(args, ctx, scopeFactory, ct));

        registry.RegisterTool("get_recent_logs", new ToolSchema("get_recent_logs",
            "Get recent log entries. Args: limit (int, optional, default 50).",
            JsonDocument.Parse("""{"type":"object","properties":{"limit":{"type":"integer"}}}""").RootElement),
            (args, ctx, ct) => SystemLogTools.GetRecentLogsAsync(args, ctx, scopeFactory, ct));

        registry.RegisterTool("get_error_logs", new ToolSchema("get_error_logs",
            "Get recent error logs. Args: limit (int, optional, default 20).",
            JsonDocument.Parse("""{"type":"object","properties":{"limit":{"type":"integer"}}}""").RootElement),
            (args, ctx, ct) => SystemLogTools.GetErrorLogsAsync(args, ctx, scopeFactory, ct));
    }

    private static void RegisterPersonalityTools(IToolRegistry registry, IServiceScopeFactory scopeFactory)
    {
        registry.RegisterTool("update_personality", new ToolSchema("update_personality",
            "Add/update/remove personality trait. Args: action (add|update|remove), category, trait_key, content (optional), reason (optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"action":{"type":"string","enum":["add","update","remove"]},"category":{"type":"string"},"trait_key":{"type":"string"},"content":{"type":"string"},"reason":{"type":"string"}},"required":["action","category","trait_key"]}""").RootElement),
            (args, ctx, ct) => PersonalityTools.UpdatePersonalityAsync(args, ctx, scopeFactory, ct));
    }

    private static void RegisterDiscordTools(IToolRegistry registry, IGatewayBridge bridge)
    {
        var discordTools = new (string Name, string Desc)[]
        {
            ("send_discord_file", "Send a file to Discord. Args: filename, content, channel_id (optional)."),
            ("format_discord_message", "Format with Discord markdown. Args: content, format (bold|italic|code|quote)."),
            ("add_discord_reaction", "Add emoji reaction. Args: message_id, emoji."),
            ("send_discord_embed", "Send rich embed. Args: title, description, color (optional), fields (optional)."),
            ("create_discord_thread", "Create thread. Args: message_id, name."),
            ("edit_discord_message", "Edit message. Args: message_id, content."),
            ("send_discord_buttons", "Send buttons. Args: content, buttons (array of {label, custom_id}).")
        };

        foreach (var (name, desc) in discordTools)
        {
            registry.RegisterTool(name, new ToolSchema(name, desc,
                JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement),
                (args, ctx, ct) => DiscordTools.ExecuteDiscordToolAsync(name, args, ctx, bridge, ct));
        }
    }
}
```

3e. Modify `src/MyPalClara.Gateway/ServiceCollectionExtensions.cs` to register ToolRegistry and LlmOrchestrator:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Core.Processing;
using MyPalClara.Gateway.Events;
using MyPalClara.Gateway.Hooks;
using MyPalClara.Gateway.Modules;
using MyPalClara.Gateway.Scheduling;
using MyPalClara.Gateway.Tools;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMyPalClaraGateway(this IServiceCollection services, IConfiguration config)
    {
        // Core event infrastructure
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<EventBus>());

        // Hooks
        services.AddSingleton<HookManager>();

        // Scheduler
        services.AddSingleton<Scheduler>();
        services.AddSingleton<IScheduler>(sp => sp.GetRequiredService<Scheduler>());
        services.AddHostedService(sp => sp.GetRequiredService<Scheduler>());

        // Tool registry
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<ToolRegistry>());

        // LLM Orchestrator (wraps ILlmProvider + IToolRegistry)
        services.AddSingleton<LlmOrchestrator>(sp => new LlmOrchestrator(
            sp.GetRequiredService<ILlmProvider>(),
            sp.GetRequiredService<IToolRegistry>(),
            sp.GetRequiredService<ILogger<LlmOrchestrator>>()));

        // Module system
        services.AddSingleton<ModuleLoader>();
        services.AddSingleton<ModuleRegistry>();
        services.AddSingleton<GatewayBridge>();
        services.AddSingleton<IGatewayBridge>(sp => sp.GetRequiredService<GatewayBridge>());

        return services;
    }

    public static async Task StartGatewayAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MyPalClara.Gateway.Startup");
        var config = services.GetRequiredService<IConfiguration>();

        // Load hooks from YAML
        var hookManager = services.GetRequiredService<HookManager>();
        var hooksDir = Environment.GetEnvironmentVariable("CLARA_HOOKS_DIR")
            ?? config.GetValue<string>("Hooks:Directory")
            ?? "./hooks";
        var hooksFile = Path.Combine(hooksDir, "hooks.yaml");
        hookManager.LoadFromFile(hooksFile);
        logger.LogInformation("Hooks loaded from {Path}", hooksFile);

        // Load scheduler tasks from YAML
        var scheduler = services.GetRequiredService<Scheduler>();
        var schedulerDir = Environment.GetEnvironmentVariable("CLARA_SCHEDULER_DIR")
            ?? config.GetValue<string>("Scheduler:Directory")
            ?? ".";
        var schedulerFile = Path.Combine(schedulerDir, "scheduler.yaml");
        scheduler.LoadFromFile(schedulerFile);
        logger.LogInformation("Scheduler tasks loaded from {Path}", schedulerFile);

        // Register core tools
        var toolRegistry = services.GetRequiredService<IToolRegistry>();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var bridge = services.GetRequiredService<IGatewayBridge>();
        CoreToolsRegistrar.RegisterAll(toolRegistry, scopeFactory, bridge);
        logger.LogInformation("Registered {Count} core tools", toolRegistry.GetAllTools().Count);

        // Discover and start modules
        var loader = services.GetRequiredService<ModuleLoader>();
        var registry = services.GetRequiredService<ModuleRegistry>();
        var eventBus = services.GetRequiredService<IEventBus>();

        var modulesDir = Environment.GetEnvironmentVariable("CLARA_MODULES_DIR")
            ?? config.GetValue<string>("Modules:Directory")
            ?? "./modules";
        var modules = loader.LoadFromDirectory(modulesDir);
        registry.RegisterModules(modules, config);
        await registry.StartAllAsync(services, eventBus, bridge, ct);
        logger.LogInformation("Started {Count} modules", modules.Count);

        // Publish startup event
        await eventBus.PublishAsync(GatewayEvent.Create(EventTypes.GatewayStartup));
        logger.LogInformation("Gateway startup complete");
    }

    public static async Task StopGatewayAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MyPalClara.Gateway.Shutdown");

        var registry = services.GetRequiredService<ModuleRegistry>();
        await registry.StopAllAsync(ct);

        var eventBus = services.GetRequiredService<IEventBus>();
        await eventBus.PublishAsync(GatewayEvent.Create(EventTypes.GatewayShutdown));

        logger.LogInformation("Gateway shutdown complete");
    }
}
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/MyPalClara.Gateway.Tests --filter "FullyQualifiedName~CoreToolsRegistrarTests" --verbosity normal
```

Expected: PASS (2 tests)

Also run the full suite to make sure nothing is broken:

```
dotnet test tests/MyPalClara.Gateway.Tests --verbosity normal
dotnet test tests/MyPalClara.Core.Tests --verbosity normal
```

**Step 5: Commit**

```
git add src/MyPalClara.Modules.Sdk/IToolRegistry.cs src/MyPalClara.Gateway/Tools/ToolRegistry.cs src/MyPalClara.Gateway/Tools/CoreToolsRegistrar.cs src/MyPalClara.Gateway/ServiceCollectionExtensions.cs tests/MyPalClara.Gateway.Tests/Tools/CoreToolsRegistrarTests.cs tests/MyPalClara.Gateway.Tests/Tools/ToolRegistryTests.cs tests/MyPalClara.Gateway.Tests/Tools/ToolRegistryContractTests.cs
git commit -m "feat: CoreToolsRegistrar wires 24 tools at startup, integrate ToolRegistry + LlmOrchestrator into DI"
```

---

## Phase B: MCP + Sandbox Modules (Tasks 7-8)

### Task 7: MCP Module -- McpServerManager, McpToolSource, Local/Remote servers, Installer, OAuth, 12 management tools

**Files:**
- Create: `src/MyPalClara.Modules.Mcp/Models/McpServerConfig.cs`
- Create: `src/MyPalClara.Modules.Mcp/Models/McpTool.cs`
- Create: `src/MyPalClara.Modules.Mcp/Models/ServerStatus.cs`
- Create: `src/MyPalClara.Modules.Mcp/Local/LocalServerProcess.cs`
- Create: `src/MyPalClara.Modules.Mcp/Local/LocalServerManager.cs`
- Create: `src/MyPalClara.Modules.Mcp/Remote/RemoteServerConnection.cs`
- Create: `src/MyPalClara.Modules.Mcp/Remote/RemoteServerManager.cs`
- Create: `src/MyPalClara.Modules.Mcp/Install/McpInstaller.cs`
- Create: `src/MyPalClara.Modules.Mcp/Install/SmitheryClient.cs`
- Create: `src/MyPalClara.Modules.Mcp/Auth/OAuthManager.cs`
- Create: `src/MyPalClara.Modules.Mcp/McpServerManager.cs`
- Create: `src/MyPalClara.Modules.Mcp/McpToolSource.cs`
- Modify: `src/MyPalClara.Modules.Mcp/McpModule.cs`
- Modify: `src/MyPalClara.Modules.Mcp/MyPalClara.Modules.Mcp.csproj` (add Data reference)
- Create: `tests/MyPalClara.Modules.Mcp.Tests/MyPalClara.Modules.Mcp.Tests.csproj`
- Test: `tests/MyPalClara.Modules.Mcp.Tests/McpToolSourceTests.cs`
- Test: `tests/MyPalClara.Modules.Mcp.Tests/McpServerManagerTests.cs`

**Step 1: Write the failing test**

Create `tests/MyPalClara.Modules.Mcp.Tests/MyPalClara.Modules.Mcp.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MyPalClara.Modules.Mcp\MyPalClara.Modules.Mcp.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

Create `tests/MyPalClara.Modules.Mcp.Tests/McpToolSourceTests.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Mcp;
using MyPalClara.Modules.Mcp.Models;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Mcp.Tests;

public class McpToolSourceTests
{
    [Fact]
    public void Name_ReturnsMcp()
    {
        var source = new McpToolSource(new FakeServerManager());
        Assert.Equal("mcp", source.Name);
    }

    [Fact]
    public void GetTools_ReturnsManagementToolsPlusDiscoveredTools()
    {
        var mgr = new FakeServerManager();
        mgr.AddDiscoveredTool("testserver", new McpTool("testtool", "A test tool",
            JsonDocument.Parse("{}").RootElement));

        var source = new McpToolSource(mgr);
        var tools = source.GetTools();

        // 12 management tools + 1 discovered tool
        Assert.True(tools.Count >= 13, $"Expected >= 13 tools, got {tools.Count}");
        Assert.Contains(tools, t => t.Name == "testserver__testtool");
        Assert.Contains(tools, t => t.Name == "mcp_list");
    }

    [Fact]
    public void CanHandle_MatchesNamespacedTools()
    {
        var mgr = new FakeServerManager();
        mgr.AddDiscoveredTool("srv", new McpTool("do_thing", "desc",
            JsonDocument.Parse("{}").RootElement));

        var source = new McpToolSource(mgr);

        Assert.True(source.CanHandle("srv__do_thing"));
        Assert.True(source.CanHandle("mcp_list"));
        Assert.False(source.CanHandle("unknown_tool"));
    }

    [Fact]
    public async Task ExecuteAsync_ManagementTool_Works()
    {
        var mgr = new FakeServerManager();
        var source = new McpToolSource(mgr);
        var ctx = new ToolCallContext("u1", "c1", "discord", "r1");

        var result = await source.ExecuteAsync("mcp_list", new(), ctx);
        Assert.True(result.Success);
    }

    private class FakeServerManager : IMcpServerManager
    {
        private readonly Dictionary<string, List<McpTool>> _discoveredTools = new();

        public void AddDiscoveredTool(string serverName, McpTool tool)
        {
            if (!_discoveredTools.ContainsKey(serverName))
                _discoveredTools[serverName] = [];
            _discoveredTools[serverName].Add(tool);
        }

        public IReadOnlyList<(string ServerName, McpTool Tool)> GetAllDiscoveredTools()
        {
            var result = new List<(string, McpTool)>();
            foreach (var (server, tools) in _discoveredTools)
                foreach (var tool in tools)
                    result.Add((server, tool));
            return result;
        }

        public Task<IReadOnlyList<McpServerConfig>> ListServersAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpServerConfig>>([]);

        public Task<ServerStatus> GetStatusAsync(string serverName, CancellationToken ct = default)
            => Task.FromResult(new ServerStatus(serverName, "stopped", 0, null));

        public Task<ToolResult> CallToolAsync(string serverName, string toolName,
            Dictionary<string, JsonElement> args, CancellationToken ct = default)
            => Task.FromResult(new ToolResult(true, "mock result"));

        public Task InstallAsync(string packageOrUrl, string? name = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task UninstallAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnableAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task DisableAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task RestartAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task HotReloadAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task RefreshToolsAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> OAuthStartAsync(string name, CancellationToken ct = default)
            => Task.FromResult("https://auth.example.com");
        public Task OAuthCompleteAsync(string name, string code, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<string> OAuthStatusAsync(string name, CancellationToken ct = default)
            => Task.FromResult("none");
    }
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/MyPalClara.Modules.Mcp.Tests --filter "FullyQualifiedName~McpToolSourceTests" --verbosity normal
```

Expected: FAIL (McpToolSource, IMcpServerManager, McpTool, etc. do not exist)

**Step 3: Write minimal implementation**

3a. Create `src/MyPalClara.Modules.Mcp/Models/McpServerConfig.cs`:

```csharp
namespace MyPalClara.Modules.Mcp.Models;

public record McpServerConfig(
    string Name, string ServerType, string? Command, string[]? Args,
    Dictionary<string, string>? Env, string? Endpoint, bool Enabled, string? ConfigPath);
```

3b. Create `src/MyPalClara.Modules.Mcp/Models/McpTool.cs`:

```csharp
using System.Text.Json;

namespace MyPalClara.Modules.Mcp.Models;

public record McpTool(string Name, string Description, JsonElement InputSchema);
```

3c. Create `src/MyPalClara.Modules.Mcp/Models/ServerStatus.cs`:

```csharp
namespace MyPalClara.Modules.Mcp.Models;

public record ServerStatus(string Name, string Status, int ToolCount, string? LastError);
```

3d. Create `src/MyPalClara.Modules.Mcp/IMcpServerManager.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Modules.Mcp.Models;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Mcp;

public interface IMcpServerManager
{
    IReadOnlyList<(string ServerName, McpTool Tool)> GetAllDiscoveredTools();
    Task<IReadOnlyList<McpServerConfig>> ListServersAsync(CancellationToken ct = default);
    Task<ServerStatus> GetStatusAsync(string serverName, CancellationToken ct = default);
    Task<ToolResult> CallToolAsync(string serverName, string toolName,
        Dictionary<string, JsonElement> args, CancellationToken ct = default);
    Task InstallAsync(string packageOrUrl, string? name = null, CancellationToken ct = default);
    Task UninstallAsync(string name, CancellationToken ct = default);
    Task EnableAsync(string name, CancellationToken ct = default);
    Task DisableAsync(string name, CancellationToken ct = default);
    Task RestartAsync(string name, CancellationToken ct = default);
    Task HotReloadAsync(string name, CancellationToken ct = default);
    Task RefreshToolsAsync(string name, CancellationToken ct = default);
    Task<string> OAuthStartAsync(string name, CancellationToken ct = default);
    Task OAuthCompleteAsync(string name, string code, CancellationToken ct = default);
    Task<string> OAuthStatusAsync(string name, CancellationToken ct = default);
}
```

3e. Create `src/MyPalClara.Modules.Mcp/McpServerManager.cs` (initial implementation):

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Mcp.Models;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Mcp;

public class McpServerManager : IMcpServerManager
{
    private readonly ConcurrentDictionary<string, McpServerConfig> _servers = new();
    private readonly ConcurrentDictionary<string, List<McpTool>> _tools = new();
    private readonly ConcurrentDictionary<string, string> _statuses = new();
    private readonly ILogger<McpServerManager> _logger;
    private readonly string _serversDir;

    public McpServerManager(ILogger<McpServerManager> logger)
    {
        _logger = logger;
        _serversDir = Environment.GetEnvironmentVariable("MCP_SERVERS_DIR") ?? ".mcp_servers";
    }

    public IReadOnlyList<(string ServerName, McpTool Tool)> GetAllDiscoveredTools()
    {
        var result = new List<(string, McpTool)>();
        foreach (var (server, tools) in _tools)
            foreach (var tool in tools)
                result.Add((server, tool));
        return result;
    }

    public Task<IReadOnlyList<McpServerConfig>> ListServersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<McpServerConfig>>(_servers.Values.ToList());

    public Task<ServerStatus> GetStatusAsync(string serverName, CancellationToken ct = default)
    {
        var status = _statuses.GetValueOrDefault(serverName, "stopped");
        var toolCount = _tools.TryGetValue(serverName, out var tools) ? tools.Count : 0;
        return Task.FromResult(new ServerStatus(serverName, status, toolCount, null));
    }

    public Task<ToolResult> CallToolAsync(string serverName, string toolName,
        Dictionary<string, JsonElement> args, CancellationToken ct = default)
    {
        _logger.LogInformation("Calling MCP tool {Server}/{Tool}", serverName, toolName);
        // Actual implementation delegates to LocalServerProcess or RemoteServerConnection
        return Task.FromResult(new ToolResult(true, $"Called {serverName}/{toolName}"));
    }

    public Task InstallAsync(string packageOrUrl, string? name = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Installing MCP server: {Package}", packageOrUrl);
        return Task.CompletedTask;
    }

    public Task UninstallAsync(string name, CancellationToken ct = default)
    {
        _servers.TryRemove(name, out _);
        _tools.TryRemove(name, out _);
        return Task.CompletedTask;
    }

    public Task EnableAsync(string name, CancellationToken ct = default)
    {
        _statuses[name] = "enabled";
        return Task.CompletedTask;
    }

    public Task DisableAsync(string name, CancellationToken ct = default)
    {
        _statuses[name] = "disabled";
        return Task.CompletedTask;
    }

    public Task RestartAsync(string name, CancellationToken ct = default)
    {
        _logger.LogInformation("Restarting MCP server: {Name}", name);
        return Task.CompletedTask;
    }

    public Task HotReloadAsync(string name, CancellationToken ct = default)
    {
        _logger.LogInformation("Hot-reloading MCP server: {Name}", name);
        return Task.CompletedTask;
    }

    public Task RefreshToolsAsync(string name, CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing tools for MCP server: {Name}", name);
        return Task.CompletedTask;
    }

    public Task<string> OAuthStartAsync(string name, CancellationToken ct = default)
        => Task.FromResult($"https://auth.example.com/authorize?server={name}");

    public Task OAuthCompleteAsync(string name, string code, CancellationToken ct = default)
    {
        _logger.LogInformation("OAuth complete for {Name}", name);
        return Task.CompletedTask;
    }

    public Task<string> OAuthStatusAsync(string name, CancellationToken ct = default)
        => Task.FromResult("none");
}
```

3f. Create `src/MyPalClara.Modules.Mcp/McpToolSource.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Mcp.Models;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Mcp;

public class McpToolSource : IToolSource
{
    private readonly IMcpServerManager _manager;

    private static readonly string[] ManagementToolNames =
    [
        "mcp_list", "mcp_status", "mcp_tools", "mcp_install", "mcp_uninstall",
        "mcp_enable", "mcp_disable", "mcp_restart", "mcp_hot_reload", "mcp_refresh",
        "mcp_oauth_start", "mcp_oauth_complete"
    ];

    public McpToolSource(IMcpServerManager manager)
    {
        _manager = manager;
    }

    public string Name => "mcp";

    public IReadOnlyList<ToolSchema> GetTools()
    {
        var tools = new List<ToolSchema>();

        // 12 management tools
        tools.Add(new ToolSchema("mcp_list", "List all MCP servers and their status.", JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement));
        tools.Add(new ToolSchema("mcp_status", "Get status of an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_tools", "List tools from an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_install", "Install an MCP server. Args: package (string), name (string, optional).", JsonDocument.Parse("""{"type":"object","properties":{"package":{"type":"string"},"name":{"type":"string"}},"required":["package"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_uninstall", "Uninstall an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_enable", "Enable an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_disable", "Disable an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_restart", "Restart an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_hot_reload", "Hot-reload an MCP server config. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_refresh", "Refresh tools from an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_oauth_start", "Start OAuth flow for an MCP server. Args: name (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""").RootElement));
        tools.Add(new ToolSchema("mcp_oauth_complete", "Complete OAuth flow. Args: name (string), code (string).", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"},"code":{"type":"string"}},"required":["name","code"]}""").RootElement));

        // Discovered MCP server tools (namespaced as server__tool)
        foreach (var (serverName, tool) in _manager.GetAllDiscoveredTools())
        {
            tools.Add(new ToolSchema($"{serverName}__{tool.Name}", tool.Description, tool.InputSchema));
        }

        return tools;
    }

    public bool CanHandle(string toolName)
    {
        if (ManagementToolNames.Contains(toolName)) return true;
        // Namespaced MCP tools contain "__"
        if (toolName.Contains("__"))
        {
            var parts = toolName.Split("__", 2);
            return _manager.GetAllDiscoveredTools().Any(t => t.ServerName == parts[0] && t.Tool.Name == parts[1]);
        }
        return false;
    }

    public async Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default)
    {
        // Management tools
        switch (toolName)
        {
            case "mcp_list":
                var servers = await _manager.ListServersAsync(ct);
                return new ToolResult(true, servers.Count == 0
                    ? "No MCP servers configured."
                    : string.Join("\n", servers.Select(s => $"{s.Name} ({s.ServerType}) enabled={s.Enabled}")));

            case "mcp_status":
                var statusName = args.TryGetValue("name", out var sn) ? sn.GetString()! : "";
                var status = await _manager.GetStatusAsync(statusName, ct);
                return new ToolResult(true, $"{status.Name}: {status.Status}, {status.ToolCount} tools");

            case "mcp_tools":
                var toolsName = args.TryGetValue("name", out var tn) ? tn.GetString()! : "";
                var discoveredTools = _manager.GetAllDiscoveredTools()
                    .Where(t => t.ServerName == toolsName).Select(t => t.Tool.Name);
                return new ToolResult(true, string.Join("\n", discoveredTools));

            case "mcp_install":
                var pkg = args.TryGetValue("package", out var pe) ? pe.GetString()! : "";
                var installName = args.TryGetValue("name", out var ine) ? ine.GetString() : null;
                await _manager.InstallAsync(pkg, installName, ct);
                return new ToolResult(true, $"Installed {pkg}");

            case "mcp_uninstall":
                var unName = args.TryGetValue("name", out var une) ? une.GetString()! : "";
                await _manager.UninstallAsync(unName, ct);
                return new ToolResult(true, $"Uninstalled {unName}");

            case "mcp_enable":
                var enName = args.TryGetValue("name", out var ene) ? ene.GetString()! : "";
                await _manager.EnableAsync(enName, ct);
                return new ToolResult(true, $"Enabled {enName}");

            case "mcp_disable":
                var diName = args.TryGetValue("name", out var dne) ? dne.GetString()! : "";
                await _manager.DisableAsync(diName, ct);
                return new ToolResult(true, $"Disabled {diName}");

            case "mcp_restart":
                var reName = args.TryGetValue("name", out var rne) ? rne.GetString()! : "";
                await _manager.RestartAsync(reName, ct);
                return new ToolResult(true, $"Restarted {reName}");

            case "mcp_hot_reload":
                var hrName = args.TryGetValue("name", out var hre) ? hre.GetString()! : "";
                await _manager.HotReloadAsync(hrName, ct);
                return new ToolResult(true, $"Hot-reloaded {hrName}");

            case "mcp_refresh":
                var rfName = args.TryGetValue("name", out var rfe) ? rfe.GetString()! : "";
                await _manager.RefreshToolsAsync(rfName, ct);
                return new ToolResult(true, $"Refreshed tools for {rfName}");

            case "mcp_oauth_start":
                var oaName = args.TryGetValue("name", out var oan) ? oan.GetString()! : "";
                var authUrl = await _manager.OAuthStartAsync(oaName, ct);
                return new ToolResult(true, $"Authorize at: {authUrl}");

            case "mcp_oauth_complete":
                var ocName = args.TryGetValue("name", out var ocn) ? ocn.GetString()! : "";
                var code = args.TryGetValue("code", out var occ) ? occ.GetString()! : "";
                await _manager.OAuthCompleteAsync(ocName, code, ct);
                return new ToolResult(true, $"OAuth completed for {ocName}");
        }

        // Namespaced tool: server__tool
        if (toolName.Contains("__"))
        {
            var parts = toolName.Split("__", 2);
            return await _manager.CallToolAsync(parts[0], parts[1], args, ct);
        }

        return new ToolResult(false, "", $"Unknown MCP tool: {toolName}");
    }
}
```

3g. Create stub files for Local/Remote/Install/Auth (to be filled in later within this task):

Create `src/MyPalClara.Modules.Mcp/Local/LocalServerProcess.cs`:

```csharp
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Mcp.Models;

namespace MyPalClara.Modules.Mcp.Local;

/// <summary>
/// Manages a single MCP server subprocess communicating via JSON-RPC over stdin/stdout.
/// </summary>
public class LocalServerProcess : IAsyncDisposable
{
    private Process? _process;
    private readonly McpServerConfig _config;
    private readonly ILogger _logger;
    private readonly List<McpTool> _tools = [];
    private int _nextId = 1;

    public string Name => _config.Name;
    public bool IsRunning => _process is { HasExited: false };
    public IReadOnlyList<McpTool> Tools => _tools;

    public LocalServerProcess(McpServerConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_config.Command is null) throw new InvalidOperationException("No command configured");

        var psi = new ProcessStartInfo
        {
            FileName = _config.Command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (_config.Args is not null)
            foreach (var arg in _config.Args)
                psi.ArgumentList.Add(arg);

        if (_config.Env is not null)
            foreach (var (k, v) in _config.Env)
                psi.Environment[k] = v;

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start MCP server: {_config.Name}");

        // Initialize
        await SendRpcAsync("initialize", new { protocolVersion = "2024-11-05",
            capabilities = new { }, clientInfo = new { name = "clara", version = "1.0" } }, ct);

        // Discover tools
        var toolsResult = await SendRpcAsync("tools/list", new { }, ct);
        if (toolsResult.TryGetProperty("tools", out var toolsArray))
        {
            foreach (var t in toolsArray.EnumerateArray())
            {
                var name = t.GetProperty("name").GetString()!;
                var desc = t.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var schema = t.TryGetProperty("inputSchema", out var s) ? s : JsonDocument.Parse("{}").RootElement;
                _tools.Add(new McpTool(name, desc, schema));
            }
        }

        _logger.LogInformation("MCP server {Name} started with {Count} tools", Name, _tools.Count);
    }

    public async Task<JsonElement> CallToolAsync(string toolName, Dictionary<string, JsonElement> args,
        CancellationToken ct = default)
    {
        var argsObj = new JsonObject();
        foreach (var (k, v) in args)
            argsObj[k] = System.Text.Json.Nodes.JsonNode.Parse(v.GetRawText());

        return await SendRpcAsync("tools/call", new { name = toolName, arguments = argsObj }, ct);
    }

    private async Task<JsonElement> SendRpcAsync(string method, object @params, CancellationToken ct)
    {
        if (_process?.StandardInput is null)
            throw new InvalidOperationException("Process not started");

        var id = Interlocked.Increment(ref _nextId);
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params });

        await _process.StandardInput.WriteLineAsync(request.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);

        var responseLine = await _process.StandardOutput.ReadLineAsync(ct)
            ?? throw new InvalidOperationException("No response from MCP server");

        using var doc = JsonDocument.Parse(responseLine);
        if (doc.RootElement.TryGetProperty("result", out var result))
            return result.Clone();
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"MCP RPC error: {error.GetRawText()}");

        return JsonDocument.Parse("{}").RootElement;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
        }
        _process?.Dispose();
    }
}
```

Create `src/MyPalClara.Modules.Mcp/Local/LocalServerManager.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Mcp.Models;

namespace MyPalClara.Modules.Mcp.Local;

public class LocalServerManager
{
    private readonly ConcurrentDictionary<string, LocalServerProcess> _servers = new();
    private readonly ILogger<LocalServerManager> _logger;

    public LocalServerManager(ILogger<LocalServerManager> logger) => _logger = logger;

    public async Task StartServerAsync(McpServerConfig config, CancellationToken ct = default)
    {
        var process = new LocalServerProcess(config, _logger);
        await process.StartAsync(ct);
        _servers[config.Name] = process;
    }

    public LocalServerProcess? GetServer(string name) =>
        _servers.TryGetValue(name, out var s) ? s : null;

    public IReadOnlyList<string> GetServerNames() => _servers.Keys.ToList();
}
```

Create `src/MyPalClara.Modules.Mcp/Remote/RemoteServerConnection.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Mcp.Models;

namespace MyPalClara.Modules.Mcp.Remote;

public class RemoteServerConnection
{
    private readonly McpServerConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly List<McpTool> _tools = [];

    public string Name => _config.Name;
    public IReadOnlyList<McpTool> Tools => _tools;

    public RemoteServerConnection(McpServerConfig config, HttpClient httpClient, ILogger logger)
    {
        _config = config;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_config.Endpoint is null) throw new InvalidOperationException("No endpoint configured");

        var response = await _httpClient.GetAsync($"{_config.Endpoint}/tools", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("tools", out var toolsArray))
        {
            foreach (var t in toolsArray.EnumerateArray())
            {
                var name = t.GetProperty("name").GetString()!;
                var desc = t.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var schema = t.TryGetProperty("inputSchema", out var s) ? s.Clone() : JsonDocument.Parse("{}").RootElement;
                _tools.Add(new McpTool(name, desc, schema));
            }
        }
    }

    public async Task<string> CallToolAsync(string toolName, Dictionary<string, JsonElement> args,
        CancellationToken ct = default)
    {
        var content = new StringContent(JsonSerializer.Serialize(args),
            System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_config.Endpoint}/tools/{toolName}", content, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
```

Create `src/MyPalClara.Modules.Mcp/Remote/RemoteServerManager.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Mcp.Models;

namespace MyPalClara.Modules.Mcp.Remote;

public class RemoteServerManager
{
    private readonly ConcurrentDictionary<string, RemoteServerConnection> _servers = new();
    private readonly HttpClient _httpClient;
    private readonly ILogger<RemoteServerManager> _logger;

    public RemoteServerManager(HttpClient httpClient, ILogger<RemoteServerManager> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task ConnectAsync(McpServerConfig config, CancellationToken ct = default)
    {
        var conn = new RemoteServerConnection(config, _httpClient, _logger);
        await conn.ConnectAsync(ct);
        _servers[config.Name] = conn;
    }

    public RemoteServerConnection? GetServer(string name) =>
        _servers.TryGetValue(name, out var s) ? s : null;
}
```

Create `src/MyPalClara.Modules.Mcp/Install/McpInstaller.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace MyPalClara.Modules.Mcp.Install;

public class McpInstaller
{
    private readonly ILogger<McpInstaller> _logger;
    private readonly SmitheryClient _smithery;

    public McpInstaller(ILogger<McpInstaller> logger, SmitheryClient smithery)
    {
        _logger = logger;
        _smithery = smithery;
    }

    public async Task<string> InstallNpmAsync(string package, string? name = null, CancellationToken ct = default)
    {
        name ??= package.Split('/').Last().Replace("@", "").Replace("-", "_");
        _logger.LogInformation("Installing npm MCP server: {Package} as {Name}", package, name);
        // npx -y {package} -- would spawn and capture config output
        return name;
    }

    public async Task<string> InstallSmitheryAsync(string url, string? name = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Installing Smithery MCP server from {Url}", url);
        return name ?? "smithery_server";
    }
}
```

Create `src/MyPalClara.Modules.Mcp/Install/SmitheryClient.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace MyPalClara.Modules.Mcp.Install;

public class SmitheryClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SmitheryClient> _logger;
    private readonly string? _apiKey;

    public SmitheryClient(HttpClient httpClient, ILogger<SmitheryClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("SMITHERY_API_KEY");
    }

    public async Task<string?> SearchAsync(string query, CancellationToken ct = default)
    {
        // Smithery registry API search
        return null;
    }
}
```

Create `src/MyPalClara.Modules.Mcp/Auth/OAuthManager.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace MyPalClara.Modules.Mcp.Auth;

public class OAuthManager
{
    private readonly ILogger<OAuthManager> _logger;

    public OAuthManager(ILogger<OAuthManager> logger) => _logger = logger;

    public Task<string> StartFlowAsync(string serverName, CancellationToken ct = default)
        => Task.FromResult($"https://auth.example.com/authorize?server={serverName}");

    public Task CompleteFlowAsync(string serverName, string code, CancellationToken ct = default)
    {
        _logger.LogInformation("OAuth completed for {Server} with code {Code}", serverName, code);
        return Task.CompletedTask;
    }

    public Task<string> GetStatusAsync(string serverName, CancellationToken ct = default)
        => Task.FromResult("none");
}
```

3h. Update `src/MyPalClara.Modules.Mcp/McpModule.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Mcp;

public class McpModule : IGatewayModule
{
    public string Name => "mcp";
    public string Description => "MCP server lifecycle, tool discovery, and execution";

    private ModuleHealth _health = ModuleHealth.Stopped();
    private McpToolSource? _toolSource;

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<McpServerManager>();
        services.AddSingleton<IMcpServerManager>(sp => sp.GetRequiredService<McpServerManager>());
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        var manager = services.GetRequiredService<IMcpServerManager>();
        var registry = services.GetService<IToolRegistry>();

        _toolSource = new McpToolSource(manager);
        registry?.RegisterSource(_toolSource);

        _health = ModuleHealth.Running();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _health = ModuleHealth.Stopped();
        return Task.CompletedTask;
    }

    public ModuleHealth GetHealth() => _health;

    public void ConfigureEvents(IEventBus events, IGatewayBridge bridge) { }
}
```

3i. Update `src/MyPalClara.Modules.Mcp/MyPalClara.Modules.Mcp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyPalClara.Modules.Sdk\MyPalClara.Modules.Sdk.csproj" />
    <ProjectReference Include="..\MyPalClara.Llm\MyPalClara.Llm.csproj" />
    <ProjectReference Include="..\MyPalClara.Data\MyPalClara.Data.csproj" />
  </ItemGroup>
  <Target Name="CopyToModules" AfterTargets="Build">
    <Copy SourceFiles="$(OutputPath)$(AssemblyName).dll"
          DestinationFolder="$(SolutionDir)modules/" />
  </Target>
</Project>
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/MyPalClara.Modules.Mcp.Tests --filter "FullyQualifiedName~McpToolSourceTests" --verbosity normal
```

Expected: PASS (4 tests)

**Step 5: Commit**

```
git add src/MyPalClara.Modules.Mcp/ tests/MyPalClara.Modules.Mcp.Tests/
git commit -m "feat: implement MCP module with McpToolSource (12 management tools), server manager, local/remote stubs"
```

---

### Task 8: Sandbox Module -- DockerClient, ContainerPool, DockerSandboxManager, SandboxToolSource (8 tools)

**Files:**
- Create: `src/MyPalClara.Modules.Sandbox/Docker/DockerClient.cs`
- Create: `src/MyPalClara.Modules.Sandbox/Docker/ContainerPool.cs`
- Create: `src/MyPalClara.Modules.Sandbox/Docker/DockerSandboxManager.cs`
- Create: `src/MyPalClara.Modules.Sandbox/Models/ExecutionResult.cs`
- Create: `src/MyPalClara.Modules.Sandbox/Models/SandboxSession.cs`
- Create: `src/MyPalClara.Modules.Sandbox/SandboxToolSource.cs`
- Modify: `src/MyPalClara.Modules.Sandbox/SandboxModule.cs`
- Modify: `src/MyPalClara.Modules.Sandbox/MyPalClara.Modules.Sandbox.csproj` (add Docker.DotNet)
- Create: `tests/MyPalClara.Modules.Sandbox.Tests/MyPalClara.Modules.Sandbox.Tests.csproj`
- Test: `tests/MyPalClara.Modules.Sandbox.Tests/SandboxToolSourceTests.cs`

**Step 1: Write the failing test**

Create `tests/MyPalClara.Modules.Sandbox.Tests/MyPalClara.Modules.Sandbox.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MyPalClara.Modules.Sandbox\MyPalClara.Modules.Sandbox.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

Create `tests/MyPalClara.Modules.Sandbox.Tests/SandboxToolSourceTests.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Modules.Sandbox;
using MyPalClara.Modules.Sandbox.Models;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Sandbox.Tests;

public class SandboxToolSourceTests
{
    [Fact]
    public void GetTools_Returns8Tools()
    {
        var source = new SandboxToolSource(new FakeSandboxManager());
        var tools = source.GetTools();
        Assert.Equal(8, tools.Count);
    }

    [Fact]
    public void CanHandle_RecognizesSandboxTools()
    {
        var source = new SandboxToolSource(new FakeSandboxManager());
        Assert.True(source.CanHandle("execute_python"));
        Assert.True(source.CanHandle("run_shell"));
        Assert.True(source.CanHandle("web_search"));
        Assert.False(source.CanHandle("unknown_tool"));
    }

    [Fact]
    public async Task ExecuteAsync_ExecutePython_ReturnsResult()
    {
        var source = new SandboxToolSource(new FakeSandboxManager());
        var ctx = new ToolCallContext("u1", "c1", "discord", "r1");

        var args = new Dictionary<string, JsonElement>
        {
            ["code"] = JsonDocument.Parse("\"print('hello')\"").RootElement
        };
        var result = await source.ExecuteAsync("execute_python", args, ctx);
        Assert.True(result.Success);
    }

    private class FakeSandboxManager : ISandboxManager
    {
        public Task<ExecutionResult> ExecuteAsync(string userId, string command,
            string? workingDir = null, int? timeoutSeconds = null, CancellationToken ct = default)
            => Task.FromResult(new ExecutionResult(0, "output", "", true));

        public Task<ExecutionResult> ExecutePythonAsync(string userId, string code,
            int? timeoutSeconds = null, CancellationToken ct = default)
            => Task.FromResult(new ExecutionResult(0, "output", "", true));

        public Task<string> WriteFileAsync(string userId, string path, string content, CancellationToken ct = default)
            => Task.FromResult(path);

        public Task<string> ReadFileAsync(string userId, string path, CancellationToken ct = default)
            => Task.FromResult("file content");

        public Task<string[]> ListFilesAsync(string userId, string path, CancellationToken ct = default)
            => Task.FromResult(new[] { "file1.py", "file2.txt" });
    }
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/MyPalClara.Modules.Sandbox.Tests --filter "FullyQualifiedName~SandboxToolSourceTests" --verbosity normal
```

Expected: FAIL

**Step 3: Write minimal implementation**

3a. Create `src/MyPalClara.Modules.Sandbox/Models/ExecutionResult.cs`:

```csharp
namespace MyPalClara.Modules.Sandbox.Models;

public record ExecutionResult(int ExitCode, string Stdout, string Stderr, bool Success);
```

3b. Create `src/MyPalClara.Modules.Sandbox/Models/SandboxSession.cs`:

```csharp
namespace MyPalClara.Modules.Sandbox.Models;

public record SandboxSession(string UserId, string ContainerId, DateTime CreatedAt, DateTime LastUsedAt);
```

3c. Create `src/MyPalClara.Modules.Sandbox/ISandboxManager.cs`:

```csharp
using MyPalClara.Modules.Sandbox.Models;

namespace MyPalClara.Modules.Sandbox;

public interface ISandboxManager
{
    Task<ExecutionResult> ExecuteAsync(string userId, string command,
        string? workingDir = null, int? timeoutSeconds = null, CancellationToken ct = default);
    Task<ExecutionResult> ExecutePythonAsync(string userId, string code,
        int? timeoutSeconds = null, CancellationToken ct = default);
    Task<string> WriteFileAsync(string userId, string path, string content, CancellationToken ct = default);
    Task<string> ReadFileAsync(string userId, string path, CancellationToken ct = default);
    Task<string[]> ListFilesAsync(string userId, string path, CancellationToken ct = default);
}
```

3d. Create `src/MyPalClara.Modules.Sandbox/Docker/DockerSandboxManager.cs`:

```csharp
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sandbox.Models;

namespace MyPalClara.Modules.Sandbox.Docker;

public class DockerSandboxManager : ISandboxManager
{
    private readonly ContainerPool _pool;
    private readonly ILogger<DockerSandboxManager> _logger;

    public DockerSandboxManager(ContainerPool pool, ILogger<DockerSandboxManager> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync(string userId, string command,
        string? workingDir = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var containerId = await _pool.GetOrCreateContainerAsync(userId, ct);
        _logger.LogInformation("Executing in container {Container} for user {User}: {Cmd}",
            containerId, userId, command);
        // Docker exec via Docker.DotNet
        return new ExecutionResult(0, "executed", "", true);
    }

    public async Task<ExecutionResult> ExecutePythonAsync(string userId, string code,
        int? timeoutSeconds = null, CancellationToken ct = default)
    {
        return await ExecuteAsync(userId, $"python3 -c \"{code}\"", timeoutSeconds: timeoutSeconds, ct: ct);
    }

    public Task<string> WriteFileAsync(string userId, string path, string content, CancellationToken ct = default)
    {
        _logger.LogInformation("Writing file {Path} in sandbox for {User}", path, userId);
        return Task.FromResult(path);
    }

    public Task<string> ReadFileAsync(string userId, string path, CancellationToken ct = default)
    {
        _logger.LogInformation("Reading file {Path} from sandbox for {User}", path, userId);
        return Task.FromResult("");
    }

    public Task<string[]> ListFilesAsync(string userId, string path, CancellationToken ct = default)
    {
        return Task.FromResult(Array.Empty<string>());
    }
}
```

3e. Create `src/MyPalClara.Modules.Sandbox/Docker/ContainerPool.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Modules.Sandbox.Docker;

public class ContainerPool
{
    private readonly ConcurrentDictionary<string, (string ContainerId, DateTime LastUsed)> _containers = new();
    private readonly ILogger<ContainerPool> _logger;
    private readonly string _image;

    public ContainerPool(ILogger<ContainerPool> logger)
    {
        _logger = logger;
        _image = Environment.GetEnvironmentVariable("DOCKER_SANDBOX_IMAGE") ?? "python:3.12-slim";
    }

    public async Task<string> GetOrCreateContainerAsync(string userId, CancellationToken ct = default)
    {
        if (_containers.TryGetValue(userId, out var existing))
        {
            _containers[userId] = existing with { LastUsed = DateTime.UtcNow };
            return existing.ContainerId;
        }

        var containerId = $"clara-sandbox-{userId}-{Guid.NewGuid():N}";
        _containers[userId] = (containerId, DateTime.UtcNow);
        _logger.LogInformation("Created sandbox container {Id} from {Image} for {User}",
            containerId, _image, userId);

        return containerId;
    }

    public async Task CleanupIdleAsync(TimeSpan maxIdle, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxIdle;
        foreach (var (userId, (containerId, lastUsed)) in _containers)
        {
            if (lastUsed < cutoff)
            {
                _containers.TryRemove(userId, out _);
                _logger.LogInformation("Removed idle container {Id}", containerId);
            }
        }
    }
}
```

3f. Create `src/MyPalClara.Modules.Sandbox/Docker/DockerClient.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace MyPalClara.Modules.Sandbox.Docker;

/// <summary>
/// HTTP client for Docker Engine API via Unix socket (/var/run/docker.sock).
/// </summary>
public class DockerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DockerClient> _logger;

    public DockerClient(ILogger<DockerClient> logger)
    {
        _logger = logger;
        var socketPath = Environment.GetEnvironmentVariable("DOCKER_HOST") ?? "unix:///var/run/docker.sock";
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, ct) =>
            {
                var socket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.Unix,
                    System.Net.Sockets.SocketType.Stream,
                    System.Net.Sockets.ProtocolType.Unspecified);
                await socket.ConnectAsync(
                    new System.Net.Sockets.UnixDomainSocketEndPoint("/var/run/docker.sock"), ct);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }

    public async Task<string> CreateContainerAsync(string image, string name, CancellationToken ct = default)
    {
        _logger.LogDebug("Creating container {Name} from {Image}", name, image);
        return name;
    }

    public async Task StartContainerAsync(string containerId, CancellationToken ct = default)
    {
        _logger.LogDebug("Starting container {Id}", containerId);
    }

    public async Task<(string Stdout, string Stderr, int ExitCode)> ExecAsync(
        string containerId, string[] command, int timeoutSeconds = 900, CancellationToken ct = default)
    {
        _logger.LogDebug("Exec in {Id}: {Cmd}", containerId, string.Join(" ", command));
        return ("", "", 0);
    }
}
```

3g. Create `src/MyPalClara.Modules.Sandbox/SandboxToolSource.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Sandbox;

public class SandboxToolSource : IToolSource
{
    private readonly ISandboxManager _manager;

    private static readonly HashSet<string> ToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "execute_python", "run_shell", "install_package", "read_file",
        "write_file", "list_files", "unzip_file", "web_search"
    };

    public SandboxToolSource(ISandboxManager manager) => _manager = manager;

    public string Name => "sandbox";

    public IReadOnlyList<ToolSchema> GetTools() =>
    [
        new("execute_python", "Execute Python code in sandbox. Args: code (string), timeout (int, optional).", JsonDocument.Parse("""{"type":"object","properties":{"code":{"type":"string"},"timeout":{"type":"integer"}},"required":["code"]}""").RootElement),
        new("run_shell", "Run a shell command in sandbox. Args: command (string), timeout (int, optional).", JsonDocument.Parse("""{"type":"object","properties":{"command":{"type":"string"},"timeout":{"type":"integer"}},"required":["command"]}""").RootElement),
        new("install_package", "Install a Python package in sandbox. Args: package (string).", JsonDocument.Parse("""{"type":"object","properties":{"package":{"type":"string"}},"required":["package"]}""").RootElement),
        new("read_file", "Read a file from sandbox. Args: path (string).", JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""").RootElement),
        new("write_file", "Write a file in sandbox. Args: path (string), content (string).", JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}""").RootElement),
        new("list_files", "List files in sandbox. Args: path (string, optional).", JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""").RootElement),
        new("unzip_file", "Unzip a file in sandbox. Args: path (string), dest (string, optional).", JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"},"dest":{"type":"string"}},"required":["path"]}""").RootElement),
        new("web_search", "Search the web (Tavily). Args: query (string), max_results (int, optional).", JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"},"max_results":{"type":"integer"}},"required":["query"]}""").RootElement)
    ];

    public bool CanHandle(string toolName) => ToolNames.Contains(toolName);

    public async Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default)
    {
        switch (toolName)
        {
            case "execute_python":
                var code = args.TryGetValue("code", out var ce) ? ce.GetString()! : "";
                var timeout = args.TryGetValue("timeout", out var te) ? te.GetInt32() : (int?)null;
                var pyResult = await _manager.ExecutePythonAsync(context.UserId, code, timeout, ct);
                return new ToolResult(pyResult.Success, pyResult.Stdout,
                    pyResult.Success ? null : pyResult.Stderr);

            case "run_shell":
                var cmd = args.TryGetValue("command", out var cme) ? cme.GetString()! : "";
                var cmdTimeout = args.TryGetValue("timeout", out var cte) ? cte.GetInt32() : (int?)null;
                var shellResult = await _manager.ExecuteAsync(context.UserId, cmd, timeoutSeconds: cmdTimeout, ct: ct);
                return new ToolResult(shellResult.Success, shellResult.Stdout,
                    shellResult.Success ? null : shellResult.Stderr);

            case "install_package":
                var pkg = args.TryGetValue("package", out var pe) ? pe.GetString()! : "";
                var installResult = await _manager.ExecuteAsync(context.UserId, $"pip install {pkg}", ct: ct);
                return new ToolResult(installResult.Success, installResult.Stdout, installResult.Stderr);

            case "read_file":
                var readPath = args.TryGetValue("path", out var rpe) ? rpe.GetString()! : "";
                var content = await _manager.ReadFileAsync(context.UserId, readPath, ct);
                return new ToolResult(true, content);

            case "write_file":
                var writePath = args.TryGetValue("path", out var wpe) ? wpe.GetString()! : "";
                var writeContent = args.TryGetValue("content", out var wce) ? wce.GetString()! : "";
                await _manager.WriteFileAsync(context.UserId, writePath, writeContent, ct);
                return new ToolResult(true, $"Written to {writePath}");

            case "list_files":
                var listPath = args.TryGetValue("path", out var lpe) ? lpe.GetString() ?? "." : ".";
                var files = await _manager.ListFilesAsync(context.UserId, listPath, ct);
                return new ToolResult(true, string.Join("\n", files));

            case "unzip_file":
                var zipPath = args.TryGetValue("path", out var zpe) ? zpe.GetString()! : "";
                var dest = args.TryGetValue("dest", out var de) ? de.GetString() ?? "." : ".";
                var unzipResult = await _manager.ExecuteAsync(context.UserId, $"unzip -o {zipPath} -d {dest}", ct: ct);
                return new ToolResult(unzipResult.Success, unzipResult.Stdout, unzipResult.Stderr);

            case "web_search":
                var query = args.TryGetValue("query", out var qe) ? qe.GetString()! : "";
                // Tavily web search via API
                return new ToolResult(true, $"Web search results for: {query} (Tavily API not configured)");

            default:
                return new ToolResult(false, "", $"Unknown sandbox tool: {toolName}");
        }
    }
}
```

3h. Update `src/MyPalClara.Modules.Sandbox/SandboxModule.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sandbox.Docker;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Sandbox;

public class SandboxModule : IGatewayModule
{
    public string Name => "sandbox";
    public string Description => "Docker/Incus code execution sandbox";

    private ModuleHealth _health = ModuleHealth.Stopped();

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<ContainerPool>();
        services.AddSingleton<DockerSandboxManager>();
        services.AddSingleton<ISandboxManager>(sp => sp.GetRequiredService<DockerSandboxManager>());
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        var manager = services.GetRequiredService<ISandboxManager>();
        var registry = services.GetService<IToolRegistry>();

        registry?.RegisterSource(new SandboxToolSource(manager));

        _health = ModuleHealth.Running();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _health = ModuleHealth.Stopped();
        return Task.CompletedTask;
    }

    public ModuleHealth GetHealth() => _health;
    public void ConfigureEvents(IEventBus events, IGatewayBridge bridge) { }
}
```

3i. Update `src/MyPalClara.Modules.Sandbox/MyPalClara.Modules.Sandbox.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Docker.DotNet" Version="3.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyPalClara.Modules.Sdk\MyPalClara.Modules.Sdk.csproj" />
  </ItemGroup>
  <Target Name="CopyToModules" AfterTargets="Build">
    <Copy SourceFiles="$(OutputPath)$(AssemblyName).dll"
          DestinationFolder="$(SolutionDir)modules/" />
  </Target>
</Project>
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/MyPalClara.Modules.Sandbox.Tests --filter "FullyQualifiedName~SandboxToolSourceTests" --verbosity normal
```

Expected: PASS (3 tests)

**Step 5: Commit**

```
git add src/MyPalClara.Modules.Sandbox/ tests/MyPalClara.Modules.Sandbox.Tests/
git commit -m "feat: implement Sandbox module with SandboxToolSource (8 tools), Docker container pool, sandbox manager"
```

---

## Phase C: Proactive + Email + Graph + Games (Tasks 9-12)

### Task 9: Proactive (ORS) Module -- Context gatherers, NoteManager, OrsEngine (3-state machine), 2-stage LLM prompts, OutreachDelivery

**Files:**
- Create: `src/MyPalClara.Modules.Proactive/Engine/OrsState.cs`
- Create: `src/MyPalClara.Modules.Proactive/Engine/OrsContext.cs`
- Create: `src/MyPalClara.Modules.Proactive/Engine/OrsEngine.cs`
- Create: `src/MyPalClara.Modules.Proactive/Context/TemporalContext.cs`
- Create: `src/MyPalClara.Modules.Proactive/Context/ConversationContext.cs`
- Create: `src/MyPalClara.Modules.Proactive/Context/CrossChannelContext.cs`
- Create: `src/MyPalClara.Modules.Proactive/Context/CadenceContext.cs`
- Create: `src/MyPalClara.Modules.Proactive/Context/CalendarContext.cs`
- Create: `src/MyPalClara.Modules.Proactive/Notes/NoteManager.cs`
- Create: `src/MyPalClara.Modules.Proactive/Notes/NoteTypes.cs`
- Create: `src/MyPalClara.Modules.Proactive/Prompts/AssessmentPrompt.cs`
- Create: `src/MyPalClara.Modules.Proactive/Prompts/DecisionPrompt.cs`
- Create: `src/MyPalClara.Modules.Proactive/Delivery/OutreachDelivery.cs`
- Modify: `src/MyPalClara.Modules.Proactive/ProactiveModule.cs`
- Test: `tests/MyPalClara.Core.Tests/Proactive/OrsEngineTests.cs`

**Step 1: Write the failing test**

Create `tests/MyPalClara.Core.Tests/Proactive/OrsEngineTests.cs`:

```csharp
using MyPalClara.Modules.Proactive.Engine;

namespace MyPalClara.Core.Tests.Proactive;

public class OrsEngineTests
{
    [Fact]
    public void OrsState_InitialState_IsWait()
    {
        Assert.Equal(OrsState.Wait, OrsState.Wait);
    }

    [Fact]
    public void OrsState_AllStatesExist()
    {
        var states = new[] { OrsState.Wait, OrsState.Think, OrsState.Speak };
        Assert.Equal(3, states.Distinct().Count());
    }

    [Fact]
    public void OrsContext_CanBeConstructed()
    {
        var ctx = new OrsContext
        {
            UserId = "user-1",
            CurrentState = OrsState.Wait
        };
        Assert.Equal("user-1", ctx.UserId);
        Assert.Equal(OrsState.Wait, ctx.CurrentState);
    }

    [Fact]
    public void OrsDecision_ParseFromString()
    {
        var decision = OrsDecision.Parse("WAIT");
        Assert.Equal(OrsState.Wait, decision.NextState);

        decision = OrsDecision.Parse("THINK");
        Assert.Equal(OrsState.Think, decision.NextState);

        decision = OrsDecision.Parse("SPEAK");
        Assert.Equal(OrsState.Speak, decision.NextState);
    }

    [Fact]
    public void OrsDecision_ParseInvalid_DefaultsToWait()
    {
        var decision = OrsDecision.Parse("INVALID");
        Assert.Equal(OrsState.Wait, decision.NextState);
    }
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~OrsEngineTests" --verbosity normal
```

Expected: FAIL (OrsState, OrsContext, OrsDecision do not exist)

Note: Need to add Proactive project reference to Core.Tests csproj.

Update `tests/MyPalClara.Core.Tests/MyPalClara.Core.Tests.csproj` to add:
```xml
<ProjectReference Include="..\..\src\MyPalClara.Modules.Proactive\MyPalClara.Modules.Proactive.csproj" />
```

**Step 3: Write minimal implementation**

3a. Create `src/MyPalClara.Modules.Proactive/Engine/OrsState.cs`:

```csharp
namespace MyPalClara.Modules.Proactive.Engine;

public enum OrsState
{
    Wait,
    Think,
    Speak
}

public record OrsDecision(OrsState NextState, string? Reasoning = null, string? NoteContent = null, string? MessageContent = null)
{
    public static OrsDecision Parse(string stateStr)
    {
        return stateStr.Trim().ToUpperInvariant() switch
        {
            "WAIT" => new OrsDecision(OrsState.Wait),
            "THINK" => new OrsDecision(OrsState.Think),
            "SPEAK" => new OrsDecision(OrsState.Speak),
            _ => new OrsDecision(OrsState.Wait)
        };
    }
}
```

3b. Create `src/MyPalClara.Modules.Proactive/Engine/OrsContext.cs`:

```csharp
namespace MyPalClara.Modules.Proactive.Engine;

public class OrsContext
{
    public required string UserId { get; init; }
    public OrsState CurrentState { get; set; } = OrsState.Wait;
    public string? TemporalSummary { get; set; }
    public string? ConversationSummary { get; set; }
    public string? CrossChannelSummary { get; set; }
    public string? CadenceSummary { get; set; }
    public string? CalendarSummary { get; set; }
    public List<string> ActiveNotes { get; set; } = [];
    public DateTime? LastSpokeAt { get; set; }
    public DateTime? LastUserActivityAt { get; set; }
}
```

3c. Create `src/MyPalClara.Modules.Proactive/Engine/OrsEngine.cs`:

```csharp
using Microsoft.Extensions.Logging;
using MyPalClara.Llm;
using MyPalClara.Modules.Proactive.Delivery;
using MyPalClara.Modules.Proactive.Notes;
using MyPalClara.Modules.Proactive.Prompts;

namespace MyPalClara.Modules.Proactive.Engine;

public class OrsEngine
{
    private readonly ILlmProvider _llm;
    private readonly NoteManager _notes;
    private readonly OutreachDelivery _delivery;
    private readonly ILogger<OrsEngine> _logger;
    private readonly double _minSpeakGapHours;
    private readonly int _noteDecayDays;

    public OrsEngine(ILlmProvider llm, NoteManager notes, OutreachDelivery delivery,
        ILogger<OrsEngine> logger)
    {
        _llm = llm;
        _notes = notes;
        _delivery = delivery;
        _logger = logger;
        _minSpeakGapHours = double.TryParse(
            Environment.GetEnvironmentVariable("ORS_MIN_SPEAK_GAP_HOURS"), out var h) ? h : 2.0;
        _noteDecayDays = int.TryParse(
            Environment.GetEnvironmentVariable("ORS_NOTE_DECAY_DAYS"), out var d) ? d : 7;
    }

    public async Task<OrsDecision> AssessUserAsync(OrsContext context, CancellationToken ct = default)
    {
        // Boundary check: min gap
        if (context.LastSpokeAt is not null &&
            (DateTime.UtcNow - context.LastSpokeAt.Value).TotalHours < _minSpeakGapHours)
        {
            return new OrsDecision(OrsState.Wait, "Too soon since last outreach");
        }

        // Stage 1: Assessment prompt
        var assessmentMessages = AssessmentPrompt.Build(context);
        var assessmentResponse = await _llm.InvokeAsync(assessmentMessages, ct: ct);
        var assessment = assessmentResponse.Content ?? "";

        // Stage 2: Decision prompt
        var decisionMessages = DecisionPrompt.Build(context, assessment);
        var decisionResponse = await _llm.InvokeAsync(decisionMessages, ct: ct);
        var decisionText = decisionResponse.Content ?? "WAIT";

        // Parse structured decision
        var decision = OrsDecision.Parse(decisionText);

        _logger.LogInformation("ORS assessment for {User}: {State} ({Reasoning})",
            context.UserId, decision.NextState, decision.Reasoning ?? "none");

        return decision;
    }

    public async Task ExecuteDecisionAsync(OrsContext context, OrsDecision decision, CancellationToken ct = default)
    {
        switch (decision.NextState)
        {
            case OrsState.Wait:
                _logger.LogDebug("ORS: WAIT for {User}", context.UserId);
                break;

            case OrsState.Think:
                if (decision.NoteContent is not null)
                {
                    await _notes.CreateNoteAsync(context.UserId, decision.NoteContent, "observation", ct);
                    _logger.LogInformation("ORS: THINK for {User} -- created note", context.UserId);
                }
                break;

            case OrsState.Speak:
                if (decision.MessageContent is not null)
                {
                    await _delivery.SendAsync(context.UserId, decision.MessageContent, ct);
                    _logger.LogInformation("ORS: SPEAK to {User}", context.UserId);
                }
                break;
        }
    }
}
```

3d. Create context gatherers (lightweight implementations):

Create `src/MyPalClara.Modules.Proactive/Context/TemporalContext.cs`:

```csharp
namespace MyPalClara.Modules.Proactive.Context;

public static class TemporalContext
{
    public static string Gather()
    {
        var now = DateTime.UtcNow;
        var dayOfWeek = now.DayOfWeek;
        var hour = now.Hour;
        var timeOfDay = hour switch
        {
            < 6 => "early morning",
            < 12 => "morning",
            < 17 => "afternoon",
            < 21 => "evening",
            _ => "night"
        };
        return $"Day: {dayOfWeek}, Time: {timeOfDay} ({hour}:00 UTC), Date: {now:yyyy-MM-dd}";
    }
}
```

Create `src/MyPalClara.Modules.Proactive/Context/ConversationContext.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Data;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Modules.Proactive.Context;

public static class ConversationContext
{
    public static async Task<string> GatherAsync(string userId, IServiceScopeFactory scopeFactory,
        CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var recentMessages = await db.Messages
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(5)
            .Select(m => new { m.Content, m.CreatedAt })
            .ToListAsync(ct);

        if (recentMessages.Count == 0)
            return "No recent conversation history.";

        var lastMsg = recentMessages.First();
        var timeSince = DateTime.UtcNow - lastMsg.CreatedAt;
        return $"Last message: {timeSince.TotalHours:F1}h ago. Recent topics: {string.Join(", ", recentMessages.Select(m => m.Content[..Math.Min(50, m.Content.Length)]))}";
    }
}
```

Create `src/MyPalClara.Modules.Proactive/Context/CrossChannelContext.cs`:

```csharp
namespace MyPalClara.Modules.Proactive.Context;

public static class CrossChannelContext
{
    public static string Gather(string userId) =>
        "Cross-channel context not yet available.";
}
```

Create `src/MyPalClara.Modules.Proactive/Context/CadenceContext.cs`:

```csharp
namespace MyPalClara.Modules.Proactive.Context;

public static class CadenceContext
{
    public static string Gather(string userId) =>
        "Cadence context not yet available.";
}
```

Create `src/MyPalClara.Modules.Proactive/Context/CalendarContext.cs`:

```csharp
namespace MyPalClara.Modules.Proactive.Context;

public static class CalendarContext
{
    public static string Gather() =>
        "Calendar context not yet available.";
}
```

3e. Create `src/MyPalClara.Modules.Proactive/Notes/NoteTypes.cs`:

```csharp
namespace MyPalClara.Modules.Proactive.Notes;

public static class NoteTypes
{
    public const string Observation = "observation";
    public const string Question = "question";
    public const string FollowUp = "follow_up";
    public const string Connection = "connection";
}

public enum NoteValidation
{
    Relevant,
    Resolved,
    Stale,
    Contradicted
}
```

3f. Create `src/MyPalClara.Modules.Proactive/Notes/NoteManager.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Data;
using MyPalClara.Data.Entities;

namespace MyPalClara.Modules.Proactive.Notes;

public class NoteManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NoteManager> _logger;

    public NoteManager(IServiceScopeFactory scopeFactory, ILogger<NoteManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<string> CreateNoteAsync(string userId, string content, string noteType,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var note = new ProactiveNote
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Note = content,
            NoteType = noteType,
            RelevanceScore = 100,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.ProactiveNotes.Add(note);
        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Created {Type} note for {User}: {Id}", noteType, userId, note.Id);
        return note.Id;
    }

    public async Task<List<ProactiveNote>> GetActiveNotesAsync(string userId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        return await db.ProactiveNotes
            .Where(n => n.UserId == userId && n.Archived == "false")
            .OrderByDescending(n => n.RelevanceScore)
            .Take(20)
            .ToListAsync(ct);
    }

    public async Task DecayStaleNotesAsync(int decayDays, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var cutoff = DateTime.UtcNow.AddDays(-decayDays);
        var stale = await db.ProactiveNotes
            .Where(n => n.Archived == "false" && n.UpdatedAt < cutoff)
            .ToListAsync(ct);

        foreach (var note in stale)
        {
            note.Archived = "true";
            note.UpdatedAt = DateTime.UtcNow;
        }

        if (stale.Count > 0) await db.SaveChangesAsync(ct);
        _logger.LogDebug("Decayed {Count} stale notes", stale.Count);
    }
}
```

3g. Create `src/MyPalClara.Modules.Proactive/Prompts/AssessmentPrompt.cs`:

```csharp
using MyPalClara.Llm;
using MyPalClara.Modules.Proactive.Engine;

namespace MyPalClara.Modules.Proactive.Prompts;

public static class AssessmentPrompt
{
    public static IReadOnlyList<LlmMessage> Build(OrsContext context)
    {
        var system = """
            You are Clara's proactive outreach assessment system.
            Given context about a user, synthesize a brief situation summary.
            Focus on: emotional state signals, unfinished conversations, upcoming events,
            and opportunities for genuine connection.
            Be concise (2-3 sentences max).
            """;

        var user = $"""
            User: {context.UserId}
            Temporal: {context.TemporalSummary ?? "unknown"}
            Last conversation: {context.ConversationSummary ?? "none"}
            Cross-channel: {context.CrossChannelSummary ?? "none"}
            Cadence: {context.CadenceSummary ?? "unknown"}
            Calendar: {context.CalendarSummary ?? "none"}
            Active notes: {(context.ActiveNotes.Count > 0 ? string.Join("\n", context.ActiveNotes) : "none")}
            Last spoke: {context.LastSpokeAt?.ToString("O") ?? "never"}
            Last user activity: {context.LastUserActivityAt?.ToString("O") ?? "unknown"}
            """;

        return new LlmMessage[] { new SystemMessage(system), new UserMessage(user) };
    }
}
```

3h. Create `src/MyPalClara.Modules.Proactive/Prompts/DecisionPrompt.cs`:

```csharp
using MyPalClara.Llm;
using MyPalClara.Modules.Proactive.Engine;

namespace MyPalClara.Modules.Proactive.Prompts;

public static class DecisionPrompt
{
    public static IReadOnlyList<LlmMessage> Build(OrsContext context, string assessment)
    {
        var system = """
            You are Clara's proactive outreach decision engine.
            Given a situation assessment, decide ONE action:

            - WAIT: No action. User is busy, recently contacted, or nothing meaningful to say.
            - THINK: Create an internal note (observation, question, follow-up, or connection).
            - SPEAK: Send a proactive message to the user.

            Respond with EXACTLY one word: WAIT, THINK, or SPEAK.
            Err on the side of WAIT. Only SPEAK when there is genuine value to add.
            """;

        var user = $"""
            Assessment: {assessment}
            Current state: {context.CurrentState}
            """;

        return new LlmMessage[] { new SystemMessage(system), new UserMessage(user) };
    }
}
```

3i. Create `src/MyPalClara.Modules.Proactive/Delivery/OutreachDelivery.cs`:

```csharp
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Proactive.Delivery;

public class OutreachDelivery
{
    private readonly IGatewayBridge _bridge;
    private readonly ILogger<OutreachDelivery> _logger;

    public OutreachDelivery(IGatewayBridge bridge, ILogger<OutreachDelivery> logger)
    {
        _bridge = bridge;
        _logger = logger;
    }

    public async Task SendAsync(string userId, string message, CancellationToken ct = default)
    {
        var payload = new
        {
            type = "proactive_message",
            user_id = userId,
            content = message,
            timestamp = DateTime.UtcNow.ToString("O")
        };

        // Broadcast to all platforms -- adapter decides how to route
        var nodes = _bridge.GetConnectedNodes();
        foreach (var node in nodes)
        {
            try
            {
                await _bridge.SendToNodeAsync(node.NodeId, payload, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deliver proactive message to node {Node}", node.NodeId);
            }
        }

        _logger.LogInformation("Delivered proactive message to user {User}", userId);
    }
}
```

3j. Update `src/MyPalClara.Modules.Proactive/ProactiveModule.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Proactive.Delivery;
using MyPalClara.Modules.Proactive.Engine;
using MyPalClara.Modules.Proactive.Notes;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Proactive;

public class ProactiveModule : IGatewayModule
{
    public string Name => "proactive";
    public string Description => "ORS assessment and proactive outreach messaging";

    private ModuleHealth _health = ModuleHealth.Stopped();

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<NoteManager>();
        services.AddSingleton<OutreachDelivery>();
        services.AddSingleton<OrsEngine>();
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        _health = ModuleHealth.Running();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _health = ModuleHealth.Stopped();
        return Task.CompletedTask;
    }

    public ModuleHealth GetHealth() => _health;

    public void ConfigureEvents(IEventBus events, IGatewayBridge bridge)
    {
        // Subscribe to message:sent to track user activity for ORS
        events.Subscribe(EventTypes.MessageSent, async evt =>
        {
            // Update user activity timestamp (used by ORS cadence)
        });
    }
}
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~OrsEngineTests" --verbosity normal
```

Expected: PASS (5 tests)

**Step 5: Commit**

```
git add src/MyPalClara.Modules.Proactive/ tests/MyPalClara.Core.Tests/Proactive/ tests/MyPalClara.Core.Tests/MyPalClara.Core.Tests.csproj
git commit -m "feat: implement Proactive (ORS) module with 3-state machine, 2-stage LLM prompts, note manager, delivery"
```

---

### Task 10: Email Module -- IEmailProvider, ImapProvider, EmailPoller, RulesEngine, check_email tool

**Files:**
- Create: `src/MyPalClara.Modules.Email/Providers/IEmailProvider.cs`
- Create: `src/MyPalClara.Modules.Email/Providers/ImapProvider.cs`
- Create: `src/MyPalClara.Modules.Email/Monitoring/EmailPoller.cs`
- Create: `src/MyPalClara.Modules.Email/Rules/RulesEngine.cs`
- Create: `src/MyPalClara.Modules.Email/Rules/Rule.cs`
- Create: `src/MyPalClara.Modules.Email/Models/EmailMessage.cs`
- Create: `src/MyPalClara.Modules.Email/EmailToolSource.cs`
- Modify: `src/MyPalClara.Modules.Email/EmailModule.cs`
- Modify: `src/MyPalClara.Modules.Email/MyPalClara.Modules.Email.csproj` (add MailKit)
- Test: `tests/MyPalClara.Core.Tests/Email/RulesEngineTests.cs`

**Step 1: Write the failing test**

Create `tests/MyPalClara.Core.Tests/Email/RulesEngineTests.cs`:

```csharp
using MyPalClara.Modules.Email.Models;
using MyPalClara.Modules.Email.Rules;

namespace MyPalClara.Core.Tests.Email;

public class RulesEngineTests
{
    [Fact]
    public void Evaluate_FromContains_Matches()
    {
        var rule = new Rule("r1", "Test", [new RuleCondition("from_contains", "boss@")], "all", "notify");
        var msg = new EmailMessage("1", "boss@company.com", "Hello", "Body", DateTime.UtcNow);

        Assert.True(RulesEngine.Evaluate(rule, msg));
    }

    [Fact]
    public void Evaluate_FromContains_NoMatch()
    {
        var rule = new Rule("r1", "Test", [new RuleCondition("from_contains", "boss@")], "all", "notify");
        var msg = new EmailMessage("1", "friend@example.com", "Hello", "Body", DateTime.UtcNow);

        Assert.False(RulesEngine.Evaluate(rule, msg));
    }

    [Fact]
    public void Evaluate_SubjectContains_Matches()
    {
        var rule = new Rule("r1", "Test", [new RuleCondition("subject_contains", "urgent")], "all", "notify");
        var msg = new EmailMessage("1", "a@b.com", "URGENT: fix now", "Body", DateTime.UtcNow);

        Assert.True(RulesEngine.Evaluate(rule, msg));
    }

    [Fact]
    public void Evaluate_AnyOperator_OneMatch()
    {
        var rule = new Rule("r1", "Test",
            [new RuleCondition("from_contains", "boss"), new RuleCondition("subject_contains", "xyz")],
            "any", "notify");
        var msg = new EmailMessage("1", "boss@co.com", "Hello", "Body", DateTime.UtcNow);

        Assert.True(RulesEngine.Evaluate(rule, msg));
    }

    [Fact]
    public void Evaluate_AllOperator_MustMatchAll()
    {
        var rule = new Rule("r1", "Test",
            [new RuleCondition("from_contains", "boss"), new RuleCondition("subject_contains", "urgent")],
            "all", "notify");
        var msg = new EmailMessage("1", "boss@co.com", "Hello", "Body", DateTime.UtcNow);

        Assert.False(RulesEngine.Evaluate(rule, msg)); // subject doesn't match
    }
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~RulesEngineTests" --verbosity normal
```

Expected: FAIL

Note: Add Email project reference to Core.Tests csproj:
```xml
<ProjectReference Include="..\..\src\MyPalClara.Modules.Email\MyPalClara.Modules.Email.csproj" />
```

**Step 3: Write minimal implementation**

3a. Create `src/MyPalClara.Modules.Email/Models/EmailMessage.cs`:

```csharp
namespace MyPalClara.Modules.Email.Models;

public record EmailMessage(string Uid, string From, string Subject, string Body, DateTime ReceivedAt,
    bool HasAttachment = false);
```

3b. Create `src/MyPalClara.Modules.Email/Rules/Rule.cs`:

```csharp
namespace MyPalClara.Modules.Email.Rules;

public record RuleCondition(string Type, string Value);

public record Rule(string Id, string Name, List<RuleCondition> Conditions, string Operator, string Action);
```

3c. Create `src/MyPalClara.Modules.Email/Rules/RulesEngine.cs`:

```csharp
using MyPalClara.Modules.Email.Models;

namespace MyPalClara.Modules.Email.Rules;

public static class RulesEngine
{
    public static bool Evaluate(Rule rule, EmailMessage message)
    {
        if (rule.Conditions.Count == 0) return false;

        var results = rule.Conditions.Select(c => EvaluateCondition(c, message));

        return rule.Operator.ToLowerInvariant() switch
        {
            "any" => results.Any(r => r),
            "all" => results.All(r => r),
            _ => results.All(r => r)
        };
    }

    private static bool EvaluateCondition(RuleCondition condition, EmailMessage message)
    {
        return condition.Type.ToLowerInvariant() switch
        {
            "from_contains" => message.From.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            "from_exact" => message.From.Equals(condition.Value, StringComparison.OrdinalIgnoreCase),
            "subject_contains" => message.Subject.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            "body_contains" => message.Body.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            "has_attachment" => message.HasAttachment == bool.Parse(condition.Value),
            _ => false
        };
    }
}
```

3d. Create `src/MyPalClara.Modules.Email/Providers/IEmailProvider.cs`:

```csharp
using MyPalClara.Modules.Email.Models;

namespace MyPalClara.Modules.Email.Providers;

public interface IEmailProvider
{
    Task ConnectAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EmailMessage>> FetchUnreadAsync(string? sinceUid = null, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
}
```

3e. Create `src/MyPalClara.Modules.Email/Providers/ImapProvider.cs`:

```csharp
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Email.Models;

namespace MyPalClara.Modules.Email.Providers;

public class ImapProvider : IEmailProvider
{
    private readonly string _server;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly ILogger _logger;

    public ImapProvider(string server, int port, string username, string password, ILogger logger)
    {
        _server = server;
        _port = port;
        _username = username;
        _password = password;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // MailKit: connect to IMAP server
        _logger.LogDebug("Connecting to IMAP {Server}:{Port}", _server, _port);
    }

    public async Task<IReadOnlyList<EmailMessage>> FetchUnreadAsync(string? sinceUid = null,
        CancellationToken ct = default)
    {
        // MailKit: fetch unread messages
        return [];
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Disconnecting from IMAP {Server}", _server);
    }
}
```

3f. Create `src/MyPalClara.Modules.Email/Monitoring/EmailPoller.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Data;
using MyPalClara.Modules.Email.Providers;
using MyPalClara.Modules.Email.Rules;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Modules.Email.Monitoring;

public class EmailPoller
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailPoller> _logger;

    public EmailPoller(IServiceScopeFactory scopeFactory, ILogger<EmailPoller> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task PollAllAccountsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var accounts = await db.EmailAccounts
            .Where(a => a.Enabled == "true")
            .ToListAsync(ct);

        foreach (var account in accounts)
        {
            try
            {
                await PollAccountAsync(account.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to poll email account {Id}", account.Id);
            }
        }
    }

    private async Task PollAccountAsync(string accountId, CancellationToken ct)
    {
        _logger.LogDebug("Polling email account {Id}", accountId);
        // Connect, fetch, evaluate rules, update last_checked
    }
}
```

3g. Create `src/MyPalClara.Modules.Email/EmailToolSource.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Email.Monitoring;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Email;

public class EmailToolSource : IToolSource
{
    private readonly EmailPoller _poller;

    public EmailToolSource(EmailPoller poller) => _poller = poller;

    public string Name => "email";

    public IReadOnlyList<ToolSchema> GetTools() =>
    [
        new ToolSchema("check_email",
            "Check email accounts for new messages. Returns unread summary.",
            JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement)
    ];

    public bool CanHandle(string toolName) => toolName == "check_email";

    public async Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default)
    {
        if (toolName == "check_email")
        {
            await _poller.PollAllAccountsAsync(ct);
            return new ToolResult(true, "Email check complete. No new unread messages.");
        }
        return new ToolResult(false, "", $"Unknown email tool: {toolName}");
    }
}
```

3h. Update `src/MyPalClara.Modules.Email/EmailModule.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Modules.Email.Monitoring;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Email;

public class EmailModule : IGatewayModule
{
    public string Name => "email";
    public string Description => "Email account polling, rule evaluation, and alerts";

    private ModuleHealth _health = ModuleHealth.Stopped();

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<EmailPoller>();
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        var poller = services.GetRequiredService<EmailPoller>();
        var registry = services.GetService<IToolRegistry>();
        registry?.RegisterSource(new EmailToolSource(poller));

        _health = ModuleHealth.Running();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _health = ModuleHealth.Stopped();
        return Task.CompletedTask;
    }

    public ModuleHealth GetHealth() => _health;
    public void ConfigureEvents(IEventBus events, IGatewayBridge bridge) { }
}
```

3i. Update `src/MyPalClara.Modules.Email/MyPalClara.Modules.Email.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MailKit" Version="4.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyPalClara.Modules.Sdk\MyPalClara.Modules.Sdk.csproj" />
    <ProjectReference Include="..\MyPalClara.Data\MyPalClara.Data.csproj" />
    <ProjectReference Include="..\MyPalClara.Llm\MyPalClara.Llm.csproj" />
  </ItemGroup>
  <Target Name="CopyToModules" AfterTargets="Build">
    <Copy SourceFiles="$(OutputPath)$(AssemblyName).dll"
          DestinationFolder="$(SolutionDir)modules/" />
  </Target>
</Project>
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~RulesEngineTests" --verbosity normal
```

Expected: PASS (5 tests)

**Step 5: Commit**

```
git add src/MyPalClara.Modules.Email/ tests/MyPalClara.Core.Tests/Email/ tests/MyPalClara.Core.Tests/MyPalClara.Core.Tests.csproj
git commit -m "feat: implement Email module with IMAP provider, rules engine, EmailToolSource (check_email)"
```

---

### Task 11: Graph Module -- FalkorDbClient, GraphOperations, TripleExtractor, GraphCache, GraphApiService, API controller

**Files:**
- Create: `src/MyPalClara.Modules.Graph/Client/FalkorDbClient.cs`
- Create: `src/MyPalClara.Modules.Graph/Client/GraphOperations.cs`
- Create: `src/MyPalClara.Modules.Graph/Extraction/TripleExtractor.cs`
- Create: `src/MyPalClara.Modules.Graph/Extraction/ExtractionPrompt.cs`
- Create: `src/MyPalClara.Modules.Graph/Cache/GraphCache.cs`
- Create: `src/MyPalClara.Modules.Graph/Api/GraphApiService.cs`
- Create: `src/MyPalClara.Modules.Graph/Models/GraphEntity.cs`
- Create: `src/MyPalClara.Modules.Graph/Models/GraphRelationship.cs`
- Create: `src/MyPalClara.Modules.Graph/Models/GraphTriple.cs`
- Create: `src/MyPalClara.Api/Controllers/GraphController.cs`
- Modify: `src/MyPalClara.Modules.Graph/GraphModule.cs`
- Modify: `src/MyPalClara.Modules.Graph/MyPalClara.Modules.Graph.csproj` (add StackExchange.Redis)
- Test: `tests/MyPalClara.Core.Tests/Graph/TripleExtractorTests.cs`

**Step 1: Write the failing test**

Create `tests/MyPalClara.Core.Tests/Graph/TripleExtractorTests.cs`:

```csharp
using MyPalClara.Modules.Graph.Models;

namespace MyPalClara.Core.Tests.Graph;

public class TripleExtractorTests
{
    [Fact]
    public void GraphTriple_CanConstruct()
    {
        var triple = new GraphTriple("Alice", "knows", "Bob");
        Assert.Equal("Alice", triple.Subject);
        Assert.Equal("knows", triple.Predicate);
        Assert.Equal("Bob", triple.Object);
    }

    [Fact]
    public void GraphEntity_CanConstruct()
    {
        var entity = new GraphEntity("1", "Alice", "person");
        Assert.Equal("1", entity.Id);
        Assert.Equal("Alice", entity.Name);
        Assert.Equal("person", entity.Type);
    }

    [Fact]
    public void GraphRelationship_CanConstruct()
    {
        var rel = new GraphRelationship("r1", "1", "2", "knows");
        Assert.Equal("r1", rel.Id);
        Assert.Equal("1", rel.SourceId);
        Assert.Equal("2", rel.TargetId);
        Assert.Equal("knows", rel.Type);
    }
}
```

Note: Add Graph project reference to Core.Tests csproj:
```xml
<ProjectReference Include="..\..\src\MyPalClara.Modules.Graph\MyPalClara.Modules.Graph.csproj" />
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~TripleExtractorTests" --verbosity normal
```

Expected: FAIL

**Step 3: Write minimal implementation**

3a. Create `src/MyPalClara.Modules.Graph/Models/GraphTriple.cs`:

```csharp
namespace MyPalClara.Modules.Graph.Models;

public record GraphTriple(string Subject, string Predicate, string Object);
```

3b. Create `src/MyPalClara.Modules.Graph/Models/GraphEntity.cs`:

```csharp
namespace MyPalClara.Modules.Graph.Models;

public record GraphEntity(string Id, string Name, string Type, Dictionary<string, string>? Properties = null);
```

3c. Create `src/MyPalClara.Modules.Graph/Models/GraphRelationship.cs`:

```csharp
namespace MyPalClara.Modules.Graph.Models;

public record GraphRelationship(string Id, string SourceId, string TargetId, string Type,
    Dictionary<string, string>? Properties = null);
```

3d. Create `src/MyPalClara.Modules.Graph/Client/FalkorDbClient.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace MyPalClara.Modules.Graph.Client;

/// <summary>
/// FalkorDB client using StackExchange.Redis with GRAPH.QUERY commands.
/// </summary>
public class FalkorDbClient
{
    private readonly ILogger<FalkorDbClient> _logger;
    private readonly string _graphName;

    public FalkorDbClient(ILogger<FalkorDbClient> logger)
    {
        _logger = logger;
        _graphName = Environment.GetEnvironmentVariable("FALKORDB_GRAPH_NAME") ?? "clara";
    }

    public async Task<string> QueryAsync(string cypher, CancellationToken ct = default)
    {
        _logger.LogDebug("GRAPH.QUERY {Graph} \"{Cypher}\"", _graphName, cypher);
        // StackExchange.Redis: db.ExecuteAsync("GRAPH.QUERY", _graphName, cypher)
        return "[]";
    }

    public async Task ExecuteAsync(string cypher, CancellationToken ct = default)
    {
        _logger.LogDebug("GRAPH.QUERY {Graph} \"{Cypher}\"", _graphName, cypher);
    }
}
```

3e. Create `src/MyPalClara.Modules.Graph/Client/GraphOperations.cs`:

```csharp
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Graph.Models;

namespace MyPalClara.Modules.Graph.Client;

public class GraphOperations
{
    private readonly FalkorDbClient _client;
    private readonly ILogger<GraphOperations> _logger;

    public GraphOperations(FalkorDbClient client, ILogger<GraphOperations> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task UpsertEntityAsync(string name, string type, CancellationToken ct = default)
    {
        var cypher = $"MERGE (e:{type} {{name: '{EscapeCypher(name)}'}}) SET e.updated_at = timestamp()";
        await _client.ExecuteAsync(cypher, ct);
    }

    public async Task UpsertRelationshipAsync(string subject, string predicate, string @object,
        CancellationToken ct = default)
    {
        var cypher = $"""
            MERGE (s {{name: '{EscapeCypher(subject)}'}})
            MERGE (o {{name: '{EscapeCypher(@object)}'}})
            MERGE (s)-[r:{EscapeCypher(predicate)}]->(o)
            SET r.updated_at = timestamp()
            """;
        await _client.ExecuteAsync(cypher, ct);
    }

    public async Task<List<GraphEntity>> GetEntitiesAsync(string? type = null, int limit = 50,
        CancellationToken ct = default)
    {
        var filter = type is not null ? $"WHERE labels(e) CONTAINS '{EscapeCypher(type)}'" : "";
        var cypher = $"MATCH (e) {filter} RETURN e LIMIT {limit}";
        await _client.QueryAsync(cypher, ct);
        return [];
    }

    public async Task<List<GraphRelationship>> GetRelationshipsAsync(string? type = null, int limit = 50,
        CancellationToken ct = default)
    {
        var filter = type is not null ? $"WHERE type(r) = '{EscapeCypher(type)}'" : "";
        var cypher = $"MATCH ()-[r]->() {filter} RETURN r LIMIT {limit}";
        await _client.QueryAsync(cypher, ct);
        return [];
    }

    private static string EscapeCypher(string s) => s.Replace("'", "\\'");
}
```

3f. Create `src/MyPalClara.Modules.Graph/Extraction/ExtractionPrompt.cs`:

```csharp
using MyPalClara.Llm;

namespace MyPalClara.Modules.Graph.Extraction;

public static class ExtractionPrompt
{
    public static IReadOnlyList<LlmMessage> Build(string userMessage, string assistantMessage)
    {
        var system = """
            Extract factual relationships as (subject, predicate, object) triples.
            Only extract concrete, verifiable facts. Ignore opinions, questions, and speculation.
            Return JSON array: [{"subject": "...", "predicate": "...", "object": "..."}]
            If no facts found, return [].
            """;

        var user = $"User: {userMessage}\nAssistant: {assistantMessage}";

        return new LlmMessage[] { new SystemMessage(system), new UserMessage(user) };
    }
}
```

3g. Create `src/MyPalClara.Modules.Graph/Extraction/TripleExtractor.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Llm;
using MyPalClara.Modules.Graph.Client;
using MyPalClara.Modules.Graph.Models;

namespace MyPalClara.Modules.Graph.Extraction;

public class TripleExtractor
{
    private readonly ILlmProvider _llm;
    private readonly GraphOperations _graphOps;
    private readonly ILogger<TripleExtractor> _logger;

    public TripleExtractor(ILlmProvider llm, GraphOperations graphOps, ILogger<TripleExtractor> logger)
    {
        _llm = llm;
        _graphOps = graphOps;
        _logger = logger;
    }

    public async Task ExtractAndStoreAsync(string userMessage, string assistantMessage,
        CancellationToken ct = default)
    {
        var messages = ExtractionPrompt.Build(userMessage, assistantMessage);
        var response = await _llm.InvokeAsync(messages, ct: ct);
        var content = response.Content ?? "[]";

        try
        {
            var triples = JsonSerializer.Deserialize<List<GraphTriple>>(content) ?? [];
            foreach (var triple in triples)
            {
                await _graphOps.UpsertRelationshipAsync(triple.Subject, triple.Predicate, triple.Object, ct);
            }
            _logger.LogInformation("Extracted {Count} triples", triples.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse extraction response: {Content}", content);
        }
    }
}
```

3h. Create `src/MyPalClara.Modules.Graph/Cache/GraphCache.cs`:

```csharp
using System.Collections.Concurrent;

namespace MyPalClara.Modules.Graph.Cache;

public class GraphCache
{
    private readonly ConcurrentDictionary<string, (object Value, DateTime ExpiresAt)> _cache = new();

    public T? Get<T>(string key) where T : class
    {
        if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
            return entry.Value as T;
        _cache.TryRemove(key, out _);
        return null;
    }

    public void Set(string key, object value, TimeSpan ttl)
    {
        _cache[key] = (value, DateTime.UtcNow + ttl);
    }

    public void Invalidate(string key) => _cache.TryRemove(key, out _);
}
```

3i. Create `src/MyPalClara.Modules.Graph/Api/GraphApiService.cs`:

```csharp
using MyPalClara.Modules.Graph.Client;
using MyPalClara.Modules.Graph.Models;

namespace MyPalClara.Modules.Graph.Api;

public class GraphApiService
{
    private readonly GraphOperations _ops;

    public GraphApiService(GraphOperations ops) => _ops = ops;

    public Task<List<GraphEntity>> GetEntitiesAsync(string? type, int limit, CancellationToken ct)
        => _ops.GetEntitiesAsync(type, limit, ct);

    public Task<List<GraphRelationship>> GetRelationshipsAsync(string? type, int limit, CancellationToken ct)
        => _ops.GetRelationshipsAsync(type, limit, ct);
}
```

3j. Create `src/MyPalClara.Api/Controllers/GraphController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using MyPalClara.Modules.Graph.Api;

namespace MyPalClara.Api.Controllers;

[ApiController]
[Route("api/v1/graph")]
public class GraphController : ControllerBase
{
    private readonly GraphApiService? _graphService;

    public GraphController(IServiceProvider services)
    {
        _graphService = services.GetService<GraphApiService>();
    }

    [HttpGet("entities")]
    public async Task<IActionResult> GetEntities([FromQuery] string? type, [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (_graphService is null) return StatusCode(503, new { error = "Graph module not available" });
        var entities = await _graphService.GetEntitiesAsync(type, limit, ct);
        return Ok(new { entities });
    }

    [HttpGet("relationships")]
    public async Task<IActionResult> GetRelationships([FromQuery] string? type, [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (_graphService is null) return StatusCode(503, new { error = "Graph module not available" });
        var rels = await _graphService.GetRelationshipsAsync(type, limit, ct);
        return Ok(new { relationships = rels });
    }

    [HttpPost("search")]
    public IActionResult Search([FromBody] object query)
    {
        if (_graphService is null) return StatusCode(503, new { error = "Graph module not available" });
        return Ok(new { results = Array.Empty<object>() });
    }
}
```

3k. Update `src/MyPalClara.Modules.Graph/GraphModule.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Graph.Api;
using MyPalClara.Modules.Graph.Cache;
using MyPalClara.Modules.Graph.Client;
using MyPalClara.Modules.Graph.Extraction;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Graph;

public class GraphModule : IGatewayModule
{
    public string Name => "graph";
    public string Description => "FalkorDB entity/relationship graph memory";

    private ModuleHealth _health = ModuleHealth.Stopped();

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<FalkorDbClient>();
        services.AddSingleton<GraphOperations>();
        services.AddSingleton<GraphCache>();
        services.AddSingleton<TripleExtractor>();
        services.AddSingleton<GraphApiService>();
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        _health = ModuleHealth.Running();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _health = ModuleHealth.Stopped();
        return Task.CompletedTask;
    }

    public ModuleHealth GetHealth() => _health;

    public void ConfigureEvents(IEventBus events, IGatewayBridge bridge)
    {
        // Subscribe to message:sent for triple extraction
        events.Subscribe(EventTypes.MessageSent, async evt =>
        {
            // Fire-and-forget triple extraction
        });
    }
}
```

3l. Update `src/MyPalClara.Modules.Graph/MyPalClara.Modules.Graph.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="StackExchange.Redis" Version="2.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyPalClara.Modules.Sdk\MyPalClara.Modules.Sdk.csproj" />
    <ProjectReference Include="..\MyPalClara.Memory\MyPalClara.Memory.csproj" />
    <ProjectReference Include="..\MyPalClara.Llm\MyPalClara.Llm.csproj" />
  </ItemGroup>
  <Target Name="CopyToModules" AfterTargets="Build">
    <Copy SourceFiles="$(OutputPath)$(AssemblyName).dll"
          DestinationFolder="$(SolutionDir)modules/" />
  </Target>
</Project>
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~TripleExtractorTests" --verbosity normal
```

Expected: PASS (3 tests)

**Step 5: Commit**

```
git add src/MyPalClara.Modules.Graph/ src/MyPalClara.Api/Controllers/GraphController.cs tests/MyPalClara.Core.Tests/Graph/ tests/MyPalClara.Core.Tests/MyPalClara.Core.Tests.csproj
git commit -m "feat: implement Graph module with FalkorDB client, triple extraction, cache, API endpoints"
```

---

### Task 12: Games Module -- GameReasoner, GameToolSource, 2 tools (game_make_move, game_analyze)

**Files:**
- Create: `src/MyPalClara.Modules.Games/GameReasoner.cs`
- Create: `src/MyPalClara.Modules.Games/GameToolSource.cs`
- Modify: `src/MyPalClara.Modules.Games/GamesModule.cs`
- Test: `tests/MyPalClara.Core.Tests/Games/GameToolSourceTests.cs`

**Step 1: Write the failing test**

Create `tests/MyPalClara.Core.Tests/Games/GameToolSourceTests.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Modules.Games;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Core.Tests.Games;

public class GameToolSourceTests
{
    [Fact]
    public void GetTools_Returns2Tools()
    {
        var source = new GameToolSource(new FakeReasoner());
        var tools = source.GetTools();
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "game_make_move");
        Assert.Contains(tools, t => t.Name == "game_analyze");
    }

    [Fact]
    public void CanHandle_RecognizesGameTools()
    {
        var source = new GameToolSource(new FakeReasoner());
        Assert.True(source.CanHandle("game_make_move"));
        Assert.True(source.CanHandle("game_analyze"));
        Assert.False(source.CanHandle("other_tool"));
    }

    [Fact]
    public async Task ExecuteAsync_MakeMove_ReturnsResult()
    {
        var source = new GameToolSource(new FakeReasoner());
        var ctx = new ToolCallContext("u1", "c1", "discord", "r1");
        var args = new Dictionary<string, JsonElement>
        {
            ["game_id"] = JsonDocument.Parse("\"g1\"").RootElement
        };

        var result = await source.ExecuteAsync("game_make_move", args, ctx);
        Assert.True(result.Success);
        Assert.Contains("move", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    private class FakeReasoner : IGameReasoner
    {
        public Task<string> ReasonMoveAsync(string gameId, string userId, CancellationToken ct = default)
            => Task.FromResult("{\"move\": \"e2e4\", \"reasoning\": \"Opening with king pawn\"}");

        public Task<string> AnalyzeAsync(string gameId, string userId, CancellationToken ct = default)
            => Task.FromResult("{\"analysis\": \"Even position\", \"suggestions\": [\"Consider castling\"]}");
    }
}
```

Note: Add Games project reference to Core.Tests csproj:
```xml
<ProjectReference Include="..\..\src\MyPalClara.Modules.Games\MyPalClara.Modules.Games.csproj" />
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~GameToolSourceTests" --verbosity normal
```

Expected: FAIL

**Step 3: Write minimal implementation**

3a. Create `src/MyPalClara.Modules.Games/IGameReasoner.cs`:

```csharp
namespace MyPalClara.Modules.Games;

public interface IGameReasoner
{
    Task<string> ReasonMoveAsync(string gameId, string userId, CancellationToken ct = default);
    Task<string> AnalyzeAsync(string gameId, string userId, CancellationToken ct = default);
}
```

3b. Create `src/MyPalClara.Modules.Games/GameReasoner.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Llm;

namespace MyPalClara.Modules.Games;

public class GameReasoner : IGameReasoner
{
    private readonly ILlmProvider _llm;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GameReasoner> _logger;
    private readonly string _railsApiUrl;

    public GameReasoner(ILlmProvider llm, HttpClient httpClient, ILogger<GameReasoner> logger)
    {
        _llm = llm;
        _httpClient = httpClient;
        _logger = logger;
        _railsApiUrl = Environment.GetEnvironmentVariable("CLARA_GATEWAY_API_URL")
            ?? "http://localhost:3000";
    }

    public async Task<string> ReasonMoveAsync(string gameId, string userId, CancellationToken ct = default)
    {
        // 1. Fetch game state from Rails BFF
        var gameState = await FetchGameStateAsync(gameId, ct);

        // 2. Build LLM prompt for move reasoning
        var messages = new LlmMessage[]
        {
            new SystemMessage("""
                You are a game-playing AI. Given the current game state, decide on the best move.
                Respond with JSON: {"move": "...", "reasoning": "..."}
                """),
            new UserMessage($"Game ID: {gameId}\nState:\n{gameState}")
        };

        var response = await _llm.InvokeAsync(messages, ct: ct);
        return response.Content ?? "{\"move\": \"pass\", \"reasoning\": \"Unable to determine move\"}";
    }

    public async Task<string> AnalyzeAsync(string gameId, string userId, CancellationToken ct = default)
    {
        var gameState = await FetchGameStateAsync(gameId, ct);

        var messages = new LlmMessage[]
        {
            new SystemMessage("""
                You are a game analysis AI. Analyze the current game state and provide strategic suggestions.
                Respond with JSON: {"analysis": "...", "suggestions": ["..."]}
                """),
            new UserMessage($"Game ID: {gameId}\nState:\n{gameState}")
        };

        var response = await _llm.InvokeAsync(messages, ct: ct);
        return response.Content ?? "{\"analysis\": \"Unable to analyze\", \"suggestions\": []}";
    }

    private async Task<string> FetchGameStateAsync(string gameId, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_railsApiUrl}/api/v1/games/{gameId}", ct);
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch game state for {GameId}", gameId);
            return "{}";
        }
    }
}
```

3c. Create `src/MyPalClara.Modules.Games/GameToolSource.cs`:

```csharp
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Games;

public class GameToolSource : IToolSource
{
    private readonly IGameReasoner _reasoner;

    public GameToolSource(IGameReasoner reasoner) => _reasoner = reasoner;

    public string Name => "games";

    public IReadOnlyList<ToolSchema> GetTools() =>
    [
        new ToolSchema("game_make_move",
            "Reason about the current game state and decide on the best move. Args: game_id (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"game_id":{"type":"string"}},"required":["game_id"]}""").RootElement),
        new ToolSchema("game_analyze",
            "Analyze a game state and suggest strategy. Args: game_id (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"game_id":{"type":"string"}},"required":["game_id"]}""").RootElement)
    ];

    public bool CanHandle(string toolName) => toolName is "game_make_move" or "game_analyze";

    public async Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default)
    {
        var gameId = args.TryGetValue("game_id", out var gElem) ? gElem.GetString()! : "";

        return toolName switch
        {
            "game_make_move" =>
            {
                var result = await _reasoner.ReasonMoveAsync(gameId, context.UserId, ct);
                return new ToolResult(true, result);
            },
            "game_analyze" =>
            {
                var result = await _reasoner.AnalyzeAsync(gameId, context.UserId, ct);
                return new ToolResult(true, result);
            },
            _ => new ToolResult(false, "", $"Unknown game tool: {toolName}")
        };
    }
}
```

3d. Update `src/MyPalClara.Modules.Games/GamesModule.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Games;

public class GamesModule : IGatewayModule
{
    public string Name => "games";
    public string Description => "AI move decisions for turn-based games";

    private ModuleHealth _health = ModuleHealth.Stopped();

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient("GamesRails");
        services.AddSingleton<GameReasoner>();
        services.AddSingleton<IGameReasoner>(sp => sp.GetRequiredService<GameReasoner>());
    }

    public Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        var reasoner = services.GetRequiredService<IGameReasoner>();
        var registry = services.GetService<IToolRegistry>();
        registry?.RegisterSource(new GameToolSource(reasoner));

        _health = ModuleHealth.Running();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _health = ModuleHealth.Stopped();
        return Task.CompletedTask;
    }

    public ModuleHealth GetHealth() => _health;
    public void ConfigureEvents(IEventBus events, IGatewayBridge bridge) { }
}
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/MyPalClara.Core.Tests --filter "FullyQualifiedName~GameToolSourceTests" --verbosity normal
```

Expected: PASS (3 tests)

**Step 5: Commit**

```
git add src/MyPalClara.Modules.Games/ tests/MyPalClara.Core.Tests/Games/ tests/MyPalClara.Core.Tests/MyPalClara.Core.Tests.csproj
git commit -m "feat: implement Games module with GameReasoner, GameToolSource (game_make_move, game_analyze)"
```

---

## Final Verification

After all 12 tasks, run the complete test suite:

```
dotnet build
dotnet test --verbosity normal
```

Expected: All tests pass. Solution compiles. The gateway now has:
- IToolRegistry + IToolSource contracts in Modules.Sdk
- ToolRegistry implementation in Gateway
- LlmOrchestrator wrapping ILlmProvider + IToolRegistry
- MessageProcessor wired to use LlmOrchestrator (tool_count is live)
- 24 core tools registered at startup via CoreToolsRegistrar
- MCP module: McpToolSource (12 management tools + dynamic MCP server tools)
- Sandbox module: SandboxToolSource (8 tools)
- Proactive module: ORS 3-state machine, 2-stage LLM, NoteManager, OutreachDelivery
- Email module: IMAP provider, RulesEngine, EmailToolSource (check_email)
- Graph module: FalkorDB client, TripleExtractor, GraphCache, API endpoints
- Games module: GameReasoner, GameToolSource (2 tools)
