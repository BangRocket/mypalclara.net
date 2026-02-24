# Modules, Hooks, and Scheduler Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add event bus, hooks, scheduler, and pluggable module system to the MyPalClara .NET gateway.

**Architecture:** Two new projects (Modules.Sdk for contracts, Gateway for runtime) plus stub module projects. Event bus is the foundation. Hooks and scheduler are core in-process services. Modules are DLLs discovered via assembly scanning at startup.

**Tech Stack:** .NET 10, System.Text.Json, YamlDotNet (YAML parsing), IHostedService, AssemblyLoadContext (module loading)

**Design doc:** `docs/plans/2026-02-24-modules-hooks-scheduler-design.md`

---

## Task 1: Scaffold Modules.Sdk Project

Create the contract project that all modules reference. Zero dependencies on Api/Core/Gateway.

**Files:**
- Create: `src/MyPalClara.Modules.Sdk/MyPalClara.Modules.Sdk.csproj`
- Create: `src/MyPalClara.Modules.Sdk/IGatewayModule.cs`
- Create: `src/MyPalClara.Modules.Sdk/IEventBus.cs`
- Create: `src/MyPalClara.Modules.Sdk/IGatewayBridge.cs`
- Create: `src/MyPalClara.Modules.Sdk/GatewayEvent.cs`
- Create: `src/MyPalClara.Modules.Sdk/EventTypes.cs`
- Create: `src/MyPalClara.Modules.Sdk/ModuleHealth.cs`
- Create: `src/MyPalClara.Modules.Sdk/IScheduler.cs`
- Create: `src/MyPalClara.Modules.Sdk/ScheduledTask.cs`
- Modify: `MyPalClara.slnx` (add project)

**Step 1: Create csproj**

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
</Project>
```

Needs `Microsoft.AspNetCore.App` for `IServiceCollection`, `IConfiguration`, `IEndpointRouteBuilder`.

**Step 2: Create GatewayEvent.cs**

```csharp
namespace MyPalClara.Modules.Sdk;

public record GatewayEvent(
    string Type,
    DateTime Timestamp,
    string? NodeId = null,
    string? Platform = null,
    string? UserId = null,
    string? ChannelId = null,
    string? RequestId = null,
    Dictionary<string, object>? Data = null)
{
    public static GatewayEvent Create(string type, Dictionary<string, object>? data = null)
        => new(type, DateTime.UtcNow, Data: data);

    public static GatewayEvent Create(string type, string? userId = null, string? channelId = null,
        string? nodeId = null, string? platform = null, string? requestId = null,
        Dictionary<string, object>? data = null)
        => new(type, DateTime.UtcNow, nodeId, platform, userId, channelId, requestId, data);
}
```

**Step 3: Create EventTypes.cs**

```csharp
namespace MyPalClara.Modules.Sdk;

public static class EventTypes
{
    // Lifecycle
    public const string GatewayStartup = "gateway:startup";
    public const string GatewayShutdown = "gateway:shutdown";

    // Adapters
    public const string AdapterConnected = "adapter:connected";
    public const string AdapterDisconnected = "adapter:disconnected";

    // Sessions
    public const string SessionStart = "session:start";
    public const string SessionEnd = "session:end";
    public const string SessionTimeout = "session:timeout";

    // Messages
    public const string MessageReceived = "message:received";
    public const string MessageSent = "message:sent";
    public const string MessageCancelled = "message:cancelled";

    // Tools
    public const string ToolStart = "tool:start";
    public const string ToolEnd = "tool:end";
    public const string ToolError = "tool:error";

    // Scheduler
    public const string ScheduledTaskRun = "scheduler:task_run";
    public const string ScheduledTaskError = "scheduler:task_error";

    // Memory
    public const string MemoryRead = "memory:read";
    public const string MemoryWrite = "memory:write";
}
```

**Step 4: Create IEventBus.cs**

```csharp
namespace MyPalClara.Modules.Sdk;

public interface IEventBus
{
    void Subscribe(string eventType, Func<GatewayEvent, Task> handler, int priority = 0);
    void Unsubscribe(string eventType, Func<GatewayEvent, Task> handler);
    Task PublishAsync(GatewayEvent evt);
    IReadOnlyList<GatewayEvent> GetRecentEvents(int limit = 100);
}
```

**Step 5: Create IGatewayBridge.cs**

```csharp
using System.Text.Json;

namespace MyPalClara.Modules.Sdk;

public interface IGatewayBridge
{
    Task SendToNodeAsync(string nodeId, object message, CancellationToken ct = default);
    Task BroadcastToPlatformAsync(string platform, object message, CancellationToken ct = default);
    void OnProtocolMessage(string messageType, Func<string, JsonElement, Task> handler);
    IReadOnlyList<ConnectedNode> GetConnectedNodes();
}

public record ConnectedNode(
    string NodeId,
    string Platform,
    string SessionId,
    List<string> Capabilities,
    DateTime ConnectedAt);
```

**Step 6: Create ModuleHealth.cs**

```csharp
namespace MyPalClara.Modules.Sdk;

public record ModuleHealth(
    string Status,
    string? LastError = null,
    DateTime? LastActivity = null,
    Dictionary<string, object>? Metrics = null)
{
    public static ModuleHealth Running(DateTime? lastActivity = null, Dictionary<string, object>? metrics = null)
        => new("running", LastActivity: lastActivity ?? DateTime.UtcNow, Metrics: metrics);
    public static ModuleHealth Stopped() => new("stopped");
    public static ModuleHealth Failed(string error) => new("failed", LastError: error);
    public static ModuleHealth Disabled() => new("disabled");
}
```

**Step 7: Create IScheduler.cs and ScheduledTask.cs**

```csharp
// IScheduler.cs
namespace MyPalClara.Modules.Sdk;

public interface IScheduler
{
    void AddTask(ScheduledTask task);
    bool RemoveTask(string name);
    bool EnableTask(string name);
    bool DisableTask(string name);
    Task RunTaskNowAsync(string name, CancellationToken ct = default);
    IReadOnlyList<ScheduledTask> GetTasks();
    IReadOnlyList<TaskResult> GetResults(int limit = 100);
}

public record TaskResult(
    string TaskName,
    bool Success,
    string? Output,
    string? Error,
    DateTime ExecutedAt,
    TimeSpan Duration);
```

```csharp
// ScheduledTask.cs
namespace MyPalClara.Modules.Sdk;

public enum TaskType { Interval, Cron, OneShot }

public class ScheduledTask
{
    public required string Name { get; init; }
    public required TaskType Type { get; init; }
    public string? Command { get; init; }
    public Func<CancellationToken, Task>? Handler { get; init; }
    public double Timeout { get; init; } = 300.0;
    public TimeSpan? Interval { get; init; }
    public string? Cron { get; init; }
    public TimeSpan? Delay { get; init; }
    public DateTime? RunAt { get; init; }
    public bool Enabled { get; set; } = true;
    public string? WorkingDir { get; init; }

    // State (managed by scheduler)
    public DateTime? LastRun { get; set; }
    public DateTime? NextRun { get; set; }
    public int RunCount { get; set; }
}
```

**Step 8: Create IGatewayModule.cs**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyPalClara.Modules.Sdk;

public interface IGatewayModule
{
    string Name { get; }
    string Description { get; }
    void ConfigureServices(IServiceCollection services, IConfiguration config);
    Task StartAsync(IServiceProvider services, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    ModuleHealth GetHealth();
    void ConfigureEvents(IEventBus events, IGatewayBridge bridge);
}
```

**Step 9: Add to solution**

Add `src/MyPalClara.Modules.Sdk/MyPalClara.Modules.Sdk.csproj` to `MyPalClara.slnx`.

**Step 10: Build and verify**

Run: `dotnet build`
Expected: 0 errors, 0 warnings

**Step 11: Commit**

```
feat: add Modules.Sdk project with contract interfaces
```

---

## Task 2: Scaffold Gateway Project with Event Bus

Create the runtime project that implements the event bus.

**Files:**
- Create: `src/MyPalClara.Gateway/MyPalClara.Gateway.csproj`
- Create: `src/MyPalClara.Gateway/Events/EventBus.cs`
- Create: `tests/MyPalClara.Gateway.Tests/MyPalClara.Gateway.Tests.csproj`
- Create: `tests/MyPalClara.Gateway.Tests/Events/EventBusTests.cs`
- Modify: `MyPalClara.slnx` (add both projects)

**Step 1: Create csproj**

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
    <PackageReference Include="YamlDotNet" Version="16.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyPalClara.Modules.Sdk\MyPalClara.Modules.Sdk.csproj" />
    <ProjectReference Include="..\MyPalClara.Core\MyPalClara.Core.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Create test csproj**

Standard xUnit test project referencing `MyPalClara.Gateway`.

**Step 3: Write EventBus tests**

```csharp
namespace MyPalClara.Gateway.Tests.Events;

using MyPalClara.Gateway.Events;
using MyPalClara.Modules.Sdk;

public class EventBusTests
{
    [Fact]
    public async Task Publish_CallsSubscribedHandler()
    {
        var bus = new EventBus();
        var called = false;
        bus.Subscribe("test:event", async evt => { called = true; });

        await bus.PublishAsync(GatewayEvent.Create("test:event"));

        Assert.True(called);
    }

    [Fact]
    public async Task Publish_HigherPriorityRunsFirst()
    {
        var bus = new EventBus();
        var order = new List<int>();
        bus.Subscribe("test:event", async evt => { order.Add(1); }, priority: 1);
        bus.Subscribe("test:event", async evt => { order.Add(10); }, priority: 10);

        await bus.PublishAsync(GatewayEvent.Create("test:event"));

        Assert.Equal(10, order[0]);
        Assert.Equal(1, order[1]);
    }

    [Fact]
    public async Task Publish_HandlerErrorDoesNotBlockOthers()
    {
        var bus = new EventBus();
        var secondCalled = false;
        bus.Subscribe("test:event", async evt => { throw new Exception("boom"); }, priority: 10);
        bus.Subscribe("test:event", async evt => { secondCalled = true; }, priority: 1);

        await bus.PublishAsync(GatewayEvent.Create("test:event"));

        Assert.True(secondCalled);
    }

    [Fact]
    public async Task Publish_DoesNotCallUnrelatedSubscribers()
    {
        var bus = new EventBus();
        var called = false;
        bus.Subscribe("other:event", async evt => { called = true; });

        await bus.PublishAsync(GatewayEvent.Create("test:event"));

        Assert.False(called);
    }

    [Fact]
    public async Task GetRecentEvents_TracksHistory()
    {
        var bus = new EventBus();
        await bus.PublishAsync(GatewayEvent.Create("test:one"));
        await bus.PublishAsync(GatewayEvent.Create("test:two"));

        var recent = bus.GetRecentEvents(10);

        Assert.Equal(2, recent.Count);
        Assert.Equal("test:one", recent[0].Type);
        Assert.Equal("test:two", recent[1].Type);
    }

    [Fact]
    public async Task Unsubscribe_RemovesHandler()
    {
        var bus = new EventBus();
        var count = 0;
        Task handler(GatewayEvent evt) { count++; return Task.CompletedTask; }
        bus.Subscribe("test:event", handler);

        await bus.PublishAsync(GatewayEvent.Create("test:event"));
        Assert.Equal(1, count);

        bus.Unsubscribe("test:event", handler);
        await bus.PublishAsync(GatewayEvent.Create("test:event"));
        Assert.Equal(1, count); // unchanged
    }
}
```

**Step 4: Run tests, verify they fail**

Run: `dotnet test tests/MyPalClara.Gateway.Tests`
Expected: FAIL (EventBus class doesn't exist yet)

**Step 5: Implement EventBus**

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Events;

public class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<string, List<(Func<GatewayEvent, Task> Handler, int Priority)>> _subscribers = new();
    private readonly List<GatewayEvent> _history = [];
    private readonly object _historyLock = new();
    private readonly ILogger<EventBus>? _logger;
    private const int MaxHistory = 100;

    public EventBus(ILogger<EventBus>? logger = null)
    {
        _logger = logger;
    }

    public void Subscribe(string eventType, Func<GatewayEvent, Task> handler, int priority = 0)
    {
        var list = _subscribers.GetOrAdd(eventType, _ => []);
        lock (list)
        {
            list.Add((handler, priority));
            list.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
    }

    public void Unsubscribe(string eventType, Func<GatewayEvent, Task> handler)
    {
        if (_subscribers.TryGetValue(eventType, out var list))
        {
            lock (list)
            {
                list.RemoveAll(s => s.Handler == handler);
            }
        }
    }

    public async Task PublishAsync(GatewayEvent evt)
    {
        lock (_historyLock)
        {
            _history.Add(evt);
            if (_history.Count > MaxHistory)
                _history.RemoveAt(0);
        }

        if (!_subscribers.TryGetValue(evt.Type, out var list))
            return;

        List<(Func<GatewayEvent, Task> Handler, int Priority)> snapshot;
        lock (list)
        {
            snapshot = [.. list];
        }

        // Run handlers sequentially in priority order (higher first)
        // Each handler is isolated — errors don't block others
        foreach (var (handler, _) in snapshot)
        {
            try
            {
                await handler(evt);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Event handler for {EventType} failed", evt.Type);
            }
        }
    }

    public IReadOnlyList<GatewayEvent> GetRecentEvents(int limit = 100)
    {
        lock (_historyLock)
        {
            var count = Math.Min(limit, _history.Count);
            return _history.GetRange(_history.Count - count, count).AsReadOnly();
        }
    }
}
```

**Step 6: Run tests, verify they pass**

Run: `dotnet test tests/MyPalClara.Gateway.Tests`
Expected: 6 PASS

**Step 7: Commit**

```
feat: add Gateway project with EventBus implementation
```

---

## Task 3: Hooks System — Shell Hook Executor

Implement YAML-driven shell hooks that fire on events.

**Files:**
- Create: `src/MyPalClara.Gateway/Hooks/Hook.cs`
- Create: `src/MyPalClara.Gateway/Hooks/HookResult.cs`
- Create: `src/MyPalClara.Gateway/Hooks/ShellExecutor.cs`
- Create: `src/MyPalClara.Gateway/Hooks/HookManager.cs`
- Create: `tests/MyPalClara.Gateway.Tests/Hooks/HookManagerTests.cs`
- Create: `tests/MyPalClara.Gateway.Tests/Hooks/ShellExecutorTests.cs`

**Step 1: Create Hook.cs and HookResult.cs**

```csharp
// Hook.cs
namespace MyPalClara.Gateway.Hooks;

public enum HookType { Shell, Code }

public class Hook
{
    public required string Name { get; init; }
    public required string Event { get; init; }
    public HookType Type { get; init; } = HookType.Shell;
    public string? Command { get; init; }
    public Func<Modules.Sdk.GatewayEvent, Task>? Handler { get; init; }
    public double Timeout { get; init; } = 30.0;
    public string? WorkingDir { get; init; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; init; }
}
```

```csharp
// HookResult.cs
namespace MyPalClara.Gateway.Hooks;

public record HookResult(
    string HookName,
    string EventType,
    bool Success,
    string? Output,
    string? Error,
    DateTime ExecutedAt,
    TimeSpan Duration);
```

**Step 2: Create ShellExecutor.cs**

Spawns a process, substitutes `${CLARA_*}` variables, enforces timeout.

```csharp
using System.Diagnostics;
using System.Text;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Hooks;

public static class ShellExecutor
{
    public static async Task<(bool Success, string Output, string Error)> ExecuteAsync(
        string command, GatewayEvent evt, string? workingDir = null,
        double timeoutSeconds = 30.0, CancellationToken ct = default)
    {
        var env = BuildEnvironment(evt);
        var expandedCommand = ExpandVariables(command, env);

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd" : "/bin/sh",
            Arguments = isWindows ? $"/c {expandedCommand}" : $"-c {expandedCommand}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory()
        };

        foreach (var (key, value) in env)
            psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return (process.ExitCode == 0, stdout.ToString().TrimEnd(), stderr.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (false, stdout.ToString().TrimEnd(), "Timed out");
        }
    }

    public static Dictionary<string, string> BuildEnvironment(GatewayEvent evt)
    {
        var env = new Dictionary<string, string>
        {
            ["CLARA_EVENT_TYPE"] = evt.Type,
            ["CLARA_TIMESTAMP"] = evt.Timestamp.ToString("O")
        };

        if (evt.NodeId != null) env["CLARA_NODE_ID"] = evt.NodeId;
        if (evt.Platform != null) env["CLARA_PLATFORM"] = evt.Platform;
        if (evt.UserId != null) env["CLARA_USER_ID"] = evt.UserId;
        if (evt.ChannelId != null) env["CLARA_CHANNEL_ID"] = evt.ChannelId;
        if (evt.RequestId != null) env["CLARA_REQUEST_ID"] = evt.RequestId;

        if (evt.Data != null)
        {
            env["CLARA_EVENT_DATA"] = System.Text.Json.JsonSerializer.Serialize(evt.Data);
            foreach (var (key, value) in evt.Data)
            {
                if (value is string or int or long or double or bool)
                    env[$"CLARA_{key.ToUpperInvariant()}"] = value.ToString()!;
            }
        }

        return env;
    }

    public static string ExpandVariables(string command, Dictionary<string, string> env)
    {
        foreach (var (key, value) in env)
            command = command.Replace($"${{{key}}}", value);
        return command;
    }
}
```

**Step 3: Create HookManager.cs**

```csharp
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sdk;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MyPalClara.Gateway.Hooks;

public class HookManager
{
    private readonly List<Hook> _hooks = [];
    private readonly List<HookResult> _results = [];
    private readonly IEventBus _eventBus;
    private readonly ILogger<HookManager> _logger;
    private readonly object _lock = new();
    private const int MaxResults = 100;

    public HookManager(IEventBus eventBus, ILogger<HookManager> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public void Register(Hook hook)
    {
        lock (_lock) { _hooks.Add(hook); }

        if (hook.Type == HookType.Shell && hook.Command != null)
        {
            _eventBus.Subscribe(hook.Event, async evt =>
            {
                if (!hook.Enabled) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (success, output, error) = await ShellExecutor.ExecuteAsync(
                    hook.Command, evt, hook.WorkingDir, hook.Timeout);
                sw.Stop();

                var result = new HookResult(hook.Name, evt.Type, success, output, error, DateTime.UtcNow, sw.Elapsed);
                AddResult(result);

                if (!success)
                    _logger.LogWarning("Hook {Name} failed: {Error}", hook.Name, error);
            }, hook.Priority);
        }
        else if (hook.Type == HookType.Code && hook.Handler != null)
        {
            var handler = hook.Handler;
            _eventBus.Subscribe(hook.Event, async evt =>
            {
                if (!hook.Enabled) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await handler(evt);
                    sw.Stop();
                    AddResult(new HookResult(hook.Name, evt.Type, true, null, null, DateTime.UtcNow, sw.Elapsed));
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    AddResult(new HookResult(hook.Name, evt.Type, false, null, ex.Message, DateTime.UtcNow, sw.Elapsed));
                    _logger.LogWarning(ex, "Code hook {Name} failed", hook.Name);
                }
            }, hook.Priority);
        }
    }

    public void Unregister(string name)
    {
        lock (_lock) { _hooks.RemoveAll(h => h.Name == name); }
    }

    public void Enable(string name)
    {
        lock (_lock) { var h = _hooks.Find(h => h.Name == name); if (h != null) h.Enabled = true; }
    }

    public void Disable(string name)
    {
        lock (_lock) { var h = _hooks.Find(h => h.Name == name); if (h != null) h.Enabled = false; }
    }

    public IReadOnlyList<Hook> GetHooks()
    {
        lock (_lock) { return [.. _hooks]; }
    }

    public IReadOnlyList<HookResult> GetResults(int limit = 100)
    {
        lock (_lock) { return _results.TakeLast(Math.Min(limit, _results.Count)).ToList(); }
    }

    public (int Total, int Enabled, int Successes, int Failures) GetStats()
    {
        lock (_lock)
        {
            return (
                _hooks.Count,
                _hooks.Count(h => h.Enabled),
                _results.Count(r => r.Success),
                _results.Count(r => !r.Success));
        }
    }

    public void LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            _logger.LogDebug("No hooks file at {Path}", path);
            return;
        }

        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var config = deserializer.Deserialize<HooksConfig>(yaml);
        if (config?.Hooks == null) return;

        foreach (var entry in config.Hooks)
        {
            Register(new Hook
            {
                Name = entry.Name,
                Event = entry.Event,
                Type = HookType.Shell,
                Command = entry.Command,
                Timeout = entry.Timeout,
                WorkingDir = entry.WorkingDir,
                Enabled = entry.Enabled,
                Priority = entry.Priority
            });
        }

        _logger.LogInformation("Loaded {Count} hooks from {Path}", config.Hooks.Count, path);
    }

    private void AddResult(HookResult result)
    {
        lock (_lock)
        {
            _results.Add(result);
            if (_results.Count > MaxResults)
                _results.RemoveAt(0);
        }
    }
}

// YAML deserialization models
internal class HooksConfig
{
    public List<HookEntry> Hooks { get; set; } = [];
}

internal class HookEntry
{
    public string Name { get; set; } = "";
    public string Event { get; set; } = "";
    public string Command { get; set; } = "";
    public double Timeout { get; set; } = 30.0;
    public string? WorkingDir { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
}
```

**Step 4: Write HookManager tests**

Test registration, enable/disable, YAML loading (with a temp file), result tracking.

**Step 5: Run tests, verify pass**

Run: `dotnet test tests/MyPalClara.Gateway.Tests`

**Step 6: Commit**

```
feat: add hooks system with shell executor and YAML loading
```

---

## Task 4: Scheduler with Cron Parser

Implement the full scheduler as IHostedService.

**Files:**
- Create: `src/MyPalClara.Gateway/Scheduling/CronParser.cs`
- Create: `src/MyPalClara.Gateway/Scheduling/Scheduler.cs`
- Create: `tests/MyPalClara.Gateway.Tests/Scheduling/CronParserTests.cs`
- Create: `tests/MyPalClara.Gateway.Tests/Scheduling/SchedulerTests.cs`

**Step 1: Write CronParser tests**

```csharp
namespace MyPalClara.Gateway.Tests.Scheduling;

using MyPalClara.Gateway.Scheduling;

public class CronParserTests
{
    [Fact]
    public void Parse_EveryMinute()
    {
        var cron = CronParser.Parse("* * * * *");
        var from = new DateTime(2026, 2, 24, 10, 30, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 10, 31, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_SpecificMinute()
    {
        var cron = CronParser.Parse("15 * * * *");
        var from = new DateTime(2026, 2, 24, 10, 30, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 11, 15, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_StepExpression()
    {
        var cron = CronParser.Parse("*/15 * * * *");
        var from = new DateTime(2026, 2, 24, 10, 7, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 10, 15, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_DailyAt9AM()
    {
        var cron = CronParser.Parse("0 9 * * *");
        var from = new DateTime(2026, 2, 24, 10, 0, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 25, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_RangeExpression()
    {
        var cron = CronParser.Parse("0 9-17 * * *");
        var from = new DateTime(2026, 2, 24, 8, 0, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 9, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Parse_ListExpression()
    {
        var cron = CronParser.Parse("0 9,12,18 * * *");
        var from = new DateTime(2026, 2, 24, 10, 0, 0, DateTimeKind.Utc);
        var next = cron.GetNextOccurrence(from);
        Assert.Equal(new DateTime(2026, 2, 24, 12, 0, 0, DateTimeKind.Utc), next);
    }
}
```

**Step 2: Implement CronParser**

Built-in 5-field parser supporting `*`, `*/N`, `N-M`, `N,M`, specific values. Returns a `CronExpression` with `GetNextOccurrence(DateTime from)`.

**Step 3: Run cron tests, verify pass**

**Step 4: Write Scheduler tests**

Test: interval task fires, cron task calculates next run, one-shot fires once, disabled tasks skip, YAML loading.

**Step 5: Implement Scheduler**

`IHostedService` with 100ms loop. Manages `ScheduledTask` instances. Shell tasks use `ShellExecutor`. C# tasks invoke handler directly. Emits events on bus.

Also implement `LoadFromFile(path)` for `scheduler.yaml` (same YamlDotNet pattern as hooks).

**Step 6: Run all tests, verify pass**

Run: `dotnet test`

**Step 7: Commit**

```
feat: add scheduler with cron parser, interval, and one-shot tasks
```

---

## Task 5: Module Loader and Registry

Implement assembly scanning, module lifecycle, and GatewayBridge.

**Files:**
- Create: `src/MyPalClara.Gateway/Modules/ModuleLoader.cs`
- Create: `src/MyPalClara.Gateway/Modules/ModuleRegistry.cs`
- Create: `src/MyPalClara.Gateway/Modules/GatewayBridge.cs`
- Create: `src/MyPalClara.Gateway/ServiceCollectionExtensions.cs`
- Create: `tests/MyPalClara.Gateway.Tests/Modules/ModuleLoaderTests.cs`

**Step 1: Create ModuleLoader.cs**

```csharp
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Modules;

public class ModuleLoader
{
    private readonly ILogger<ModuleLoader> _logger;

    public ModuleLoader(ILogger<ModuleLoader> logger)
    {
        _logger = logger;
    }

    public List<IGatewayModule> LoadFromDirectory(string modulesDir)
    {
        var modules = new List<IGatewayModule>();

        if (!Directory.Exists(modulesDir))
        {
            _logger.LogDebug("Modules directory not found: {Dir}", modulesDir);
            return modules;
        }

        foreach (var dll in Directory.GetFiles(modulesDir, "*.dll"))
        {
            try
            {
                var context = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(dll), isCollectible: false);
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dll));

                foreach (var type in assembly.GetExportedTypes())
                {
                    if (type.IsAbstract || !typeof(IGatewayModule).IsAssignableFrom(type))
                        continue;

                    if (Activator.CreateInstance(type) is IGatewayModule module)
                    {
                        modules.Add(module);
                        _logger.LogInformation("Discovered module: {Name} ({Description}) from {Dll}",
                            module.Name, module.Description, Path.GetFileName(dll));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load module from {Dll}", Path.GetFileName(dll));
            }
        }

        return modules;
    }
}
```

**Step 2: Create ModuleRegistry.cs**

Manages module lifecycle: configure, start, stop, health tracking. Reads enabled/disabled from `IConfiguration["Modules:{name}"]`.

**Step 3: Create GatewayBridge.cs**

Wraps `GatewayServer` and `NodeRegistry` from Core to implement `IGatewayBridge`. Maps `NodeInfo` to `ConnectedNode`. Routes `OnProtocolMessage` subscriptions to `GatewayServer` dispatch.

**Step 4: Create ServiceCollectionExtensions.cs**

`AddMyPalClaraGateway()` extension method that registers: `EventBus` as `IEventBus`, `HookManager`, `Scheduler` as `IScheduler`, `ModuleLoader`, `ModuleRegistry`, `GatewayBridge` as `IGatewayBridge`.

**Step 5: Write ModuleLoader tests**

Test with a temp directory containing no DLLs (empty list), and test the scanning logic.

**Step 6: Run tests, verify pass**

**Step 7: Commit**

```
feat: add module loader, registry, and gateway bridge
```

---

## Task 6: Wire Gateway into Program.cs

Integrate the new Gateway project into the Api host startup.

**Files:**
- Modify: `src/MyPalClara.Api/MyPalClara.Api.csproj` (add Gateway reference)
- Modify: `src/MyPalClara.Api/Program.cs`
- Modify: `src/MyPalClara.Core/GatewayWiring.cs` (emit events on bus)
- Modify: `MyPalClara.slnx` (if not already updated)

**Step 1: Add project reference**

Add `<ProjectReference Include="..\MyPalClara.Gateway\MyPalClara.Gateway.csproj" />` to Api csproj.

**Step 2: Update Program.cs**

Add after `builder.Services.AddMyPalClara()`:

```csharp
// Register Gateway runtime (event bus, hooks, scheduler, modules)
builder.Services.AddMyPalClaraGateway(builder.Configuration);
```

After `app.Services.WireGatewayEvents()`:

```csharp
// Start modules, load hooks, start scheduler
await app.Services.StartGatewayAsync();
```

Before `app.Run()` — register shutdown:

```csharp
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    app.Services.StopGatewayAsync().GetAwaiter().GetResult();
});
```

**Step 3: Update GatewayWiring.cs**

After message processing events are wired, emit events on the bus:
- `adapter:connected` when a node registers
- `message:received` when a message arrives
- `message:sent` when response ends

This requires resolving `IEventBus` from the service provider and calling `PublishAsync`.

**Step 4: Build and verify**

Run: `dotnet build`
Expected: 0 errors

**Step 5: Commit**

```
feat: wire gateway runtime into Program.cs startup
```

---

## Task 7: Stub Module Projects

Create empty module projects that demonstrate the contract. Each builds to a DLL that the gateway can discover.

**Files:**
- Create: `src/MyPalClara.Modules.Mcp/MyPalClara.Modules.Mcp.csproj`
- Create: `src/MyPalClara.Modules.Mcp/McpModule.cs`
- Create: `src/MyPalClara.Modules.Sandbox/MyPalClara.Modules.Sandbox.csproj`
- Create: `src/MyPalClara.Modules.Sandbox/SandboxModule.cs`
- Create: `src/MyPalClara.Modules.Proactive/MyPalClara.Modules.Proactive.csproj`
- Create: `src/MyPalClara.Modules.Proactive/ProactiveModule.cs`
- Create: `src/MyPalClara.Modules.Email/MyPalClara.Modules.Email.csproj`
- Create: `src/MyPalClara.Modules.Email/EmailModule.cs`
- Create: `src/MyPalClara.Modules.Graph/MyPalClara.Modules.Graph.csproj`
- Create: `src/MyPalClara.Modules.Graph/GraphModule.cs`
- Create: `src/MyPalClara.Modules.Games/MyPalClara.Modules.Games.csproj`
- Create: `src/MyPalClara.Modules.Games/GamesModule.cs`
- Modify: `MyPalClara.slnx` (add all module projects)

**Step 1: Create each module csproj**

Each references `MyPalClara.Modules.Sdk` and sets output to `modules/` via a post-build copy:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyPalClara.Modules.Sdk\MyPalClara.Modules.Sdk.csproj" />
  </ItemGroup>
  <Target Name="CopyToModules" AfterTargets="Build">
    <Copy SourceFiles="$(OutputPath)$(AssemblyName).dll"
          DestinationFolder="$(SolutionDir)modules/" />
  </Target>
</Project>
```

Modules that need DB access also reference `MyPalClara.Data`. Modules that need LLM also reference `MyPalClara.Llm`.

**Step 2: Create each stub module class**

Example pattern (repeat for all 6):

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Mcp;

public class McpModule : IGatewayModule
{
    public string Name => "mcp";
    public string Description => "MCP server lifecycle, tool discovery, and execution";

    private ModuleHealth _health = ModuleHealth.Stopped();

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }

    public async Task StartAsync(IServiceProvider services, CancellationToken ct)
    {
        _health = ModuleHealth.Running();
        // TODO: Initialize MCP server manager
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _health = ModuleHealth.Stopped();
    }

    public ModuleHealth GetHealth() => _health;

    public void ConfigureEvents(IEventBus events, IGatewayBridge bridge)
    {
        // TODO: Subscribe to mcp_* protocol messages via bridge
    }
}
```

**Step 3: Add all projects to solution**

**Step 4: Build, verify modules/ directory gets populated**

Run: `dotnet build`
Then: `ls modules/` should show 6 DLLs

**Step 5: Commit**

```
feat: add 6 stub module projects (mcp, sandbox, proactive, email, graph, games)
```

---

## Task 8: Update Documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `src/MyPalClara.Api/appsettings.json`

**Step 1: Update appsettings.json**

Add module enable/disable config and hooks/scheduler paths:

```json
{
  "Modules": {
    "mcp": true,
    "sandbox": false,
    "proactive": false,
    "email": false,
    "graph": false,
    "games": false
  },
  "Hooks": {
    "Directory": "./hooks"
  },
  "Scheduler": {
    "Directory": "."
  }
}
```

**Step 2: Update CLAUDE.md**

Add sections for: event bus, hooks, scheduler, module system, project structure update.

**Step 3: Update README.md**

Add module system overview and configuration section.

**Step 4: Add modules/ to .gitignore**

Module DLLs are build output, not source.

**Step 5: Build, verify everything clean**

Run: `dotnet build && dotnet test`

**Step 6: Commit**

```
docs: update documentation for modules, hooks, and scheduler
```
