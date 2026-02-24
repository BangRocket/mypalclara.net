# CLAUDE.md

Guidance for Claude Code working with this repository.

## Project Overview

MyPalClara is a .NET 10 reimplementation of the MyPalClara Python gateway. It provides a WebSocket server for platform adapters and an HTTP API for the web UI, with LLM orchestration and Rook memory system integration.

## Quick Reference

```bash
# Build
dotnet build

# Run (development)
dotnet run --project src/MyPalClara.Api

# Run tests
dotnet test

# Docker
docker-compose up
```

## Architecture

```
MyPalClara.Api        -> ASP.NET Core host (HTTP API + WebSocket)
MyPalClara.Core       -> WebSocket server, message router, processing pipeline
MyPalClara.Data       -> EF Core entities + DbContext (schema-only, no migrations)
MyPalClara.Memory     -> Rook memory system (Qdrant, embeddings, FSRS, fact extraction)
MyPalClara.Llm        -> LLM provider abstraction (Anthropic, OpenAI-compatible)
```

### Key Design Decisions

- **Schema owned by Python Alembic** -- EF Core uses Fluent API mapping only, NO migrations
- **Session.Archived is string** ("true"/"false"), not bool -- matches Python schema
- **Two ports**: API (18790) + WebSocket (18789) on same Kestrel host
- **Singleton services**: GatewayServer, MessageRouter, MessageProcessor connected via events
- **IServiceScopeFactory pattern**: MessageProcessor creates scoped DbContext per request
- **FSRS-6**: Memory scoring uses power-law forgetting curve with 21 weights
- **Smart ingestion**: Similarity thresholds (0.95 skip, 0.75 update, 0.60 supersede)
- **Rook LLM**: Separate provider config (ROOK_PROVIDER/ROOK_MODEL) from chat LLM

### Data Flow

```
Adapter (Discord etc.) -> WebSocket -> GatewayServer
  -> OnMessageReceived event -> GatewayWiring handler
  -> MessageRouter.SubmitAsync (debounce + dedup)
  -> MessageProcessor.ProcessAsync (LLM streaming)
  -> GatewayServer.SendAsync (response chunks back to adapter)
  -> Background: fact extraction + smart ingestion
```

### Wire Compatibility

All responses must match the Python gateway exactly:
- HTTP API: same routes, same JSON shapes, snake_case naming
- WebSocket: same 28 message types, same field names
- Database: same tables/columns (SQLite dev, PostgreSQL prod)
- Qdrant: same collection (clara_memories), same payload fields

## Environment Variables

### Required
- `OPENAI_API_KEY` -- Always required (embeddings)
- `LLM_PROVIDER` -- anthropic, openrouter, nanogpt, openai, azure
- Provider-specific API key (e.g., `ANTHROPIC_API_KEY`)

### Gateway
- `CLARA_GATEWAY_HOST` -- Default: 127.0.0.1
- `CLARA_GATEWAY_PORT` -- WebSocket port, default: 18789
- `CLARA_GATEWAY_API_PORT` -- HTTP API port, default: 18790
- `CLARA_GATEWAY_SECRET` -- Optional auth secret
- `GATEWAY_API_CORS_ORIGINS` -- Comma-separated origins

### Database
- `DATABASE_URL` -- PostgreSQL URL (default: SQLite at data/clara.db)

### Qdrant
- `QDRANT_HOST` -- Default: localhost
- `QDRANT_PORT` -- Default: 6333
- `ROOK_COLLECTION_NAME` -- Default: clara_memories

### LLM Tiers
- `MODEL_TIER` -- Default tier: high, mid, low
- `ANTHROPIC_MODEL_HIGH`, `_MID`, `_LOW` -- Tier-specific models
- Same pattern for OPENROUTER_MODEL_*, NANOGPT_MODEL_*, etc.

### Rook (Memory Extraction)
- `ROOK_PROVIDER` -- Default: openrouter
- `ROOK_MODEL` -- Default: openai/gpt-4o-mini

## Key Patterns

- All entities in `MyPalClara.Data.Entities/` -- 32 files
- Controllers in `MyPalClara.Api.Controllers/` -- 6 controllers
- Protocol records in `MyPalClara.Core.Protocol.Messages` -- wire-compatible
- Event wiring in `MyPalClara.Core.GatewayWiring` -- connects singletons
- DI registration: `AddClaraLlm()`, `AddClaraMemory()`, `AddMyPalClara()`

## Testing

```bash
dotnet test                                    # All tests
dotnet test tests/MyPalClara.Data.Tests      # Specific project
```

## Event Bus

Foundation for hooks, scheduler, and module communication.

- **Interface**: `IEventBus` in `MyPalClara.Modules.Sdk`
- **Implementation**: `EventBus` in `MyPalClara.Gateway`
- **Payload**: `GatewayEvent` record -- Type, Timestamp, NodeId, Platform, UserId, ChannelId, RequestId, Data dict
- **Execution**: All handlers run concurrently via `Task.WhenAll`, errors isolated per handler
- **Priority**: Higher priority runs first
- **Diagnostics**: Last 100 events kept in memory

### Event Types

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

## Hooks System

Shell and C# hooks triggered by event bus events.

- **HookManager** in `MyPalClara.Gateway` -- loads YAML, registers shell hooks as event subscribers
- **ShellHookExecutor** -- spawns `Process` per hook invocation with timeout enforcement
- **YAML location**: `Hooks:Directory` from config (default: `./hooks/hooks.yaml`)
- Shell hooks receive event data as `CLARA_*` environment variables (`CLARA_EVENT_TYPE`, `CLARA_USER_ID`, `CLARA_EVENT_DATA`, etc.)
- C# modules register hooks directly via `IEventBus.Subscribe()` in `ConfigureEvents`

## Scheduler

Background `IHostedService` running interval, cron, and one-shot tasks.

- **Scheduler** in `MyPalClara.Gateway` -- 100ms tick loop
- **CronParser** -- built-in 5-field cron (`*`, `*/N`, `N-M`, `N,M`)
- **Task types**: `Interval` (every N seconds), `Cron` (5-field expression), `OneShot` (delay or specific time)
- **YAML location**: `Scheduler:Directory` from config (default: `./scheduler.yaml`)
- C# modules register tasks via `IScheduler.AddTask()`
- Emits `scheduler:task_run` and `scheduler:task_error` events

## Module System

Pluggable services discovered at startup from the `modules/` directory.

- **Contract**: `IGatewayModule` in `MyPalClara.Modules.Sdk` -- Name, ConfigureServices, StartAsync/StopAsync, GetHealth, ConfigureEvents
- **Bridge**: `IGatewayBridge` -- modules send/receive WebSocket messages and query connected nodes
- **Discovery**: `ModuleLoader` scans `Modules:Directory` for DLLs, finds `IGatewayModule` implementations
- **Enable/disable**: `appsettings.json` `"Modules"` section -- key per module name (true/false)
- **Crash isolation**: Each module's `StartAsync` wrapped in try/catch; failed modules marked in `ModuleHealth`, gateway continues
- **Health**: `ModuleHealth` record -- Status (running/stopped/failed/disabled), LastError, LastActivity, Metrics

### Project Structure (New)

```
src/
  MyPalClara.Modules.Sdk/      # Contract: IGatewayModule, IEventBus, IGatewayBridge, event types
  MyPalClara.Gateway/           # Runtime: EventBus, HookManager, Scheduler, ModuleLoader, CronParser
  MyPalClara.Modules.Mcp/       # Module: MCP server lifecycle, tool discovery
  MyPalClara.Modules.Sandbox/   # Module: Docker/Incus code execution
  MyPalClara.Modules.Proactive/ # Module: ORS assessment, proactive outreach
  MyPalClara.Modules.Email/     # Module: Account polling, alerts
  MyPalClara.Modules.Graph/     # Module: FalkorDB entity/relationship graph
  MyPalClara.Modules.Games/     # Module: AI move decisions for games
```

### Key Separation

- **Modules.Sdk** -- the contract. Modules reference only this (plus Data/Llm/Memory as needed). No reference to Api or Core.
- **Gateway** -- the runtime. References Sdk + Core. Implements event bus, hooks, scheduler, module loader.
- **Api** -- references Gateway. Wires everything in `Program.cs`.
- **Modules.*** -- reference Sdk only. Build to DLL in `modules/` directory.
