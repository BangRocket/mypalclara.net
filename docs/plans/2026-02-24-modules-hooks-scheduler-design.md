# Design: Gateway Modules, Hooks, and Scheduler

**Date**: 2026-02-24
**Status**: Approved

## Overview

Add hooks system and scheduler to the gateway core, plus a pluggable module system for optional services (MCP, sandbox, proactive messaging, email, graph memory, games) that run alongside platform adapters.

## Decisions

- **Module lifecycle**: Hybrid. Hooks + scheduler are core in-process. Optional modules are `IHostedService` with crash isolation. Platform adapters remain subprocesses.
- **Module discovery**: Assembly scanning. Modules are DLLs in a `modules/` directory, loaded via reflection at startup.
- **HTTP routes**: Gateway owns all routes. Modules expose service interfaces that controllers call.
- **WebSocket access**: Modules can tap into the WebSocket event stream directly via `IGatewayBridge`.
- **Hooks**: C# + Shell. Event bus is the core abstraction. Shell hooks from YAML are a built-in handler.
- **Scheduler**: Full port. Interval, cron, and one-shot. YAML config + C# registered tasks. Built-in cron parser.

---

## Section 1: Event Bus

The foundation everything else builds on.

```csharp
public interface IEventBus
{
    void Subscribe(string eventType, Func<GatewayEvent, Task> handler, int priority = 0);
    Task PublishAsync(GatewayEvent evt);
}
```

### Event Types

Matching Python `EventType` enum:

| Category | Events |
|----------|--------|
| Lifecycle | `gateway:startup`, `gateway:shutdown` |
| Adapters | `adapter:connected`, `adapter:disconnected` |
| Sessions | `session:start`, `session:end`, `session:timeout` |
| Messages | `message:received`, `message:sent`, `message:cancelled` |
| Tools | `tool:start`, `tool:end`, `tool:error` |
| Scheduler | `scheduler:task_run`, `scheduler:task_error` |
| Memory | `memory:read`, `memory:write` |
| Custom | any string |

### Event Payload

```csharp
public record GatewayEvent(
    string Type,
    DateTime Timestamp,
    string? NodeId = null,
    string? Platform = null,
    string? UserId = null,
    string? ChannelId = null,
    string? RequestId = null,
    Dictionary<string, object>? Data = null);
```

Shell hooks receive these as `CLARA_*` environment variables:
- `CLARA_EVENT_TYPE`, `CLARA_TIMESTAMP`, `CLARA_NODE_ID`, `CLARA_PLATFORM`
- `CLARA_USER_ID`, `CLARA_CHANNEL_ID`, `CLARA_REQUEST_ID`
- `CLARA_EVENT_DATA` (full JSON)
- Individual `CLARA_*` keys from Data dict (if scalar values)

### Execution Model

- Higher priority runs first
- All handlers run concurrently via `Task.WhenAll`
- Error in one handler doesn't block others
- Last 100 events kept in memory for diagnostics

---

## Section 2: Hooks System

Two registration paths, same event bus.

### Path 1: YAML Shell Hooks

Loaded from `CLARA_HOOKS_DIR/hooks.yaml`:

```yaml
hooks:
  - name: log-sessions
    event: session:start
    command: echo "Session started for ${CLARA_USER_ID}"
    timeout: 30
    working_dir: ./logs
    enabled: true
    priority: 0
```

Each hook spawns a `Process` on event fire, with `${CLARA_*}` variable substitution, timeout enforcement, and result tracking.

### Path 2: C# Handlers

From modules via `ConfigureEvents`:

```csharp
events.Subscribe("session:start", async (evt) => {
    // Module code runs directly
}, priority: 10);
```

### HookManager

- `LoadFromFile(path)` -- parse YAML, register shell subscribers
- `Register(hook)` / `Unregister(name)` / `Enable(name)` / `Disable(name)`
- `GetHooks()` -- list all (shell + code)
- `GetResults(limit)` -- recent execution results (last 100)
- `GetStats()` -- counts, success rates

---

## Section 3: Scheduler

Runs as `IHostedService`. Loop checks every 100ms.

### Task Types

- **Interval**: run every N seconds
- **Cron**: run on cron expression (5-field: minute hour day_of_month month day_of_week)
- **OneShot**: run once after delay or at specific time

### Registration

**YAML** (`CLARA_SCHEDULER_DIR/scheduler.yaml`):

```yaml
tasks:
  - name: cleanup-sessions
    type: interval
    interval: 3600
    command: python -m scripts.cleanup
    timeout: 300
    enabled: true

  - name: daily-summary
    type: cron
    cron: "0 9 * * *"
    command: python -m scripts.daily_summary

  - name: startup-check
    type: one_shot
    delay: 30
    command: curl http://localhost:18790/api/v1/health
```

**C# from modules**:

```csharp
scheduler.AddTask(new ScheduledTask {
    Name = "email-poll",
    Type = TaskType.Interval,
    Interval = TimeSpan.FromMinutes(5),
    Handler = async () => await CheckEmails(),
    Enabled = true
});
```

### Cron Parser

Built-in. Supports: `*`, `*/N`, `N-M`, `N,M`. Standard 5-field format. Calculates next run time.

### Events

Emits `scheduler:task_run` and `scheduler:task_error` on the event bus.

### Management

`AddTask`, `RemoveTask`, `EnableTask`, `DisableTask`, `RunTaskNow`, `GetTasks`, `GetResults`.

---

## Section 4: Module System

### Discovery

At startup, gateway scans `CLARA_MODULES_DIR` (default: `./modules`) for `.dll` files. Each assembly is loaded and scanned for types implementing `IGatewayModule`.

### Contract

```csharp
public interface IGatewayModule
{
    string Name { get; }
    string Description { get; }

    // DI registration (called before app.Build())
    void ConfigureServices(IServiceCollection services, IConfiguration config);

    // Lifecycle
    Task StartAsync(IServiceProvider services, CancellationToken ct);
    Task StopAsync(CancellationToken ct);

    // Health
    ModuleHealth GetHealth();

    // Wire into event bus + WebSocket stream
    void ConfigureEvents(IEventBus events, IGatewayBridge bridge);
}
```

### IGatewayBridge

The module's window into the WebSocket world:

```csharp
public interface IGatewayBridge
{
    // Send protocol messages to adapters
    Task SendToNodeAsync(string nodeId, object message, CancellationToken ct = default);
    Task BroadcastToPlatformAsync(string platform, object message, CancellationToken ct = default);

    // Subscribe to incoming protocol messages by type
    void OnProtocolMessage(string messageType, Func<string, JsonElement, Task> handler);

    // Access node registry
    IReadOnlyList<NodeInfo> GetConnectedNodes();
}
```

### Startup Sequence

1. Scan `modules/` for assemblies, find `IGatewayModule` types
2. Instantiate each module
3. Check `appsettings.json`: `"Modules": { "mcp": true, "sandbox": false }`
4. For enabled modules: call `ConfigureServices` (before `builder.Build()`)
5. After build: call `ConfigureEvents` with event bus + bridge
6. Call `StartAsync` for each (wrapped in try/catch)
7. Log module health status

### Crash Isolation

Each module's `StartAsync` runs in its own `try/catch`. If a module throws, it's marked `Failed` in `ModuleHealth` and the gateway continues. Background loops in modules catch their own exceptions with exponential backoff.

### ModuleHealth

```csharp
public record ModuleHealth(
    string Status,              // "running", "stopped", "failed", "disabled"
    string? LastError,
    DateTime? LastActivity,
    Dictionary<string, object>? Metrics);
```

---

## Section 5: Project Structure

```
mypalclara.net/
  src/
    MyPalClara.Api/              # (existing) HTTP host, controllers
    MyPalClara.Core/             # (existing) WebSocket, router, processor
    MyPalClara.Data/             # (existing) EF Core entities
    MyPalClara.Llm/              # (existing) LLM providers
    MyPalClara.Memory/           # (existing) Rook, embeddings, FSRS

    MyPalClara.Modules.Sdk/      # IGatewayModule, IEventBus, IGatewayBridge,
                                 # ModuleHealth, event types, base classes

    MyPalClara.Gateway/          # Event bus impl, HookManager, Scheduler,
                                 # ModuleLoader, ModuleRegistry, GatewayBridge,
                                 # CronParser, shell hook executor

    # Modules (each builds to own DLL, copied to modules/)
    MyPalClara.Modules.Mcp/
    MyPalClara.Modules.Sandbox/
    MyPalClara.Modules.Proactive/
    MyPalClara.Modules.Email/
    MyPalClara.Modules.Graph/
    MyPalClara.Modules.Games/
```

### Key Separation

- **MyPalClara.Modules.Sdk** -- the contract. Modules reference only this (+ Data/Llm/Memory as needed). No reference to Api or Core.
- **MyPalClara.Gateway** -- the runtime. References Sdk + Core. Implements event bus, hooks, scheduler, module loader.
- **MyPalClara.Api** -- references Gateway. Wires into Program.cs. Owns all HTTP controllers.
- **MyPalClara.Modules.*** -- references Sdk (+ optionally Data/Llm/Memory). Builds to DLL in `modules/`.

Post-build step copies each module's DLL to `modules/` so the gateway discovers them. Deleting a module project means the gateway starts without that feature.

---

## Planned Modules

| Module | Purpose | Gateway Dependencies |
|--------|---------|---------------------|
| Mcp | MCP server lifecycle, tool discovery, tool execution | Llm (for tool calling), WebSocket (mcp_* messages) |
| Sandbox | Docker/Incus code execution | None (standalone process management) |
| Proactive | ORS assessment loop, outreach generation | Llm, Memory, Data (DB models), WebSocket (proactive messages) |
| Email | Account polling, rule evaluation, alerts | Data (DB models), WebSocket (alert notifications) |
| Graph | FalkorDB entity/relationship graph | Memory (embeddings), Llm (triple extraction) |
| Games | AI move decisions for turn-based games | Llm (move reasoning) |
