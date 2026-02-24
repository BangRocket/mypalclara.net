# Design: Tool Execution System and Module Implementations

**Date**: 2026-02-24
**Status**: Approved

## Overview

Add tool execution to the .NET gateway processing pipeline and implement all 6 gateway modules to achieve 1:1 feature parity with the Python gateway. This covers: tool calling loop, centralized tool registry, 30 core tools, MCP server management, Docker sandbox, proactive messaging (ORS), email monitoring, graph memory, and game reasoning.

## Decisions

- **Tool execution**: Unified orchestrator. Always uses `InvokeAsync` with tools. Simulated streaming (chunked text). No real SSE streaming during tool loops.
- **Tool registry**: Centralized `IToolRegistry` in Modules.Sdk. Modules register via `IToolSource`. Registry lives in Gateway.
- **Core tools**: All except browser (30 tools). Browser via Playwright MCP server instead.
- **MCP**: Full port. Local stdio + remote HTTP + installer (npm/Smithery) + OAuth.
- **Sandbox**: Docker only. Interface supports future Incus addition.
- **Proactive (ORS)**: Full port. 3-state machine, 2-stage LLM prompting, note system, all context signals.
- **Email**: Full port. IMAP polling, rules engine, multi-provider OAuth.
- **Graph**: Full port. FalkorDB client, Redis cache, LLM triple extraction, API endpoints.
- **Games**: Minimal. LLM reasoning tools only; game state managed by Rails BFF.

---

## Section 1: Tool Execution System

Three new components in the core pipeline.

### IToolRegistry (in Modules.Sdk)

```csharp
public interface IToolRegistry
{
    void RegisterTool(string name, ToolSchema schema, Func<ToolCallContext, Task<ToolResult>> handler);
    void RegisterSource(IToolSource source);
    void UnregisterTool(string name);
    IReadOnlyList<ToolSchema> GetAllTools(ToolFilter? filter = null);
    Task<ToolResult> ExecuteAsync(string name, Dictionary<string, JsonElement> args, ToolCallContext context, CancellationToken ct = default);
}

public interface IToolSource
{
    string Name { get; }
    IReadOnlyList<ToolSchema> GetTools();
    bool CanHandle(string toolName);
    Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args, ToolCallContext context, CancellationToken ct = default);
}

public record ToolCallContext(string UserId, string ChannelId, string Platform, string RequestId);
public record ToolResult(bool Success, string Output, string? Error = null);
public record ToolFilter(string? Platform = null, List<string>? Capabilities = null);
```

Routing order: exact name match in registered tools first, then iterate `IToolSource` instances (MCP checks for `__` namespace, sandbox checks for its tool names, etc.).

### LlmOrchestrator (in Core)

Wraps `ILlmProvider` + `IToolRegistry`. Replaces the direct `StreamAsync` call in MessageProcessor.

```csharp
public class LlmOrchestrator
{
    public async IAsyncEnumerable<OrchestratorEvent> GenerateAsync(
        IReadOnlyList<LlmMessage> messages,
        ToolCallContext toolContext,
        string tier = "mid",
        CancellationToken ct = default);
}
```

Events yielded: `TextChunk(string text)`, `ToolStart(string name, int step)`, `ToolResult(string name, bool success, string preview)`, `Complete(string fullText, int toolCount)`.

Flow:
1. Get tools from registry: `_registry.GetAllTools(filter)`
2. Call `_llm.InvokeAsync(messages, tools)`
3. If `response.HasToolCalls`: for each tool call -> yield ToolStart -> `_registry.ExecuteAsync()` -> yield ToolResult -> append to messages -> loop (up to `MAX_TOOL_ITERATIONS`, default 75)
4. If no tool calls: yield text as `TextChunk` events (simulated streaming, ~50 char chunks)
5. Yield `Complete`

Config: `MAX_TOOL_ITERATIONS` (75), `MAX_TOOL_RESULT_CHARS` (50,000), `AUTO_CONTINUE_ENABLED` (true), `AUTO_CONTINUE_MAX` (3).

### MessageProcessor Changes

Replace the `StreamAsync` block with `LlmOrchestrator.GenerateAsync`. Map events to WebSocket protocol messages (ToolStartMessage, ToolResultMessage, ResponseChunkMessage). Track `toolCount` for ResponseEnd.

---

## Section 2: Core Tools

30 tools across 7 groups, registered into `IToolRegistry` at startup via `CoreToolsRegistrar`.

### Terminal Tools (2)

| Tool | Handler |
|------|---------|
| `execute_command` | Spawn process via ShellExecutor, capture stdout/stderr, enforce timeout (default 30s) |
| `get_command_history` | Return last N commands from in-memory ring buffer |

### File Storage Tools (4)

| Tool | Handler |
|------|---------|
| `save_to_local` | Write to `CLARA_FILES_DIR/{user_id}/`, S3 upload if configured |
| `list_local_files` | Local dir listing or S3 prefix scan |
| `read_local_file` | Local read or S3 GET |
| `delete_local_file` | Local delete or S3 DELETE |

S3 support via `AWSSDK.S3`. Falls back to local filesystem.

### Process Management Tools (5)

| Tool | Handler |
|------|---------|
| `process_start` | Spawn background process, track in ProcessManager singleton |
| `process_status` | PID, running state, uptime |
| `process_output` | Last N lines from output ring buffer (1000 lines) |
| `process_stop` | Kill process tree, optional force |
| `process_list` | List all tracked processes |

### Chat History Tools (2)

| Tool | Handler |
|------|---------|
| `search_chat_history` | Full-text search on Message table via EF Core |
| `get_chat_history` | Recent messages with time range, user, channel filters |

### System Log Tools (3)

| Tool | Handler |
|------|---------|
| `search_logs` | Query log table by keyword/logger/level (PostgreSQL only) |
| `get_recent_logs` | Last N log entries |
| `get_error_logs` | Recent errors with tracebacks |

### Personality Tool (1)

| Tool | Handler |
|------|---------|
| `update_personality` | Add/update/remove traits in PersonalityTrait table |

### Discord-Specific Tools (7)

| Tool | Handler |
|------|---------|
| `send_discord_file` | Forward via WebSocket `tool_action` message to adapter |
| `format_discord_message` | Format with markdown/embeds |
| `add_discord_reaction` | Add emoji reaction |
| `send_discord_embed` | Rich embed message |
| `create_discord_thread` | Create thread from message |
| `edit_discord_message` | Edit previous message |
| `send_discord_buttons` | Interactive button components |

Discord tools send a `tool_action` WebSocket message to the adapter, block until adapter responds (with timeout).

### MCP Management Tools (12)

Registered by MCP module via `IToolSource`. See Section 3.

---

## Section 3: MCP Module

Full MCP server management. Registers as `IToolSource` for dynamic tool discovery.

### Project Structure

```
MyPalClara.Modules.Mcp/
  McpModule.cs
  McpServerManager.cs       — Unified local + remote manager
  McpToolSource.cs           — IToolSource adapter
  Local/
    LocalServerProcess.cs    — Single stdio subprocess lifecycle
    LocalServerManager.cs    — Manages all local servers
  Remote/
    RemoteServerConnection.cs — HTTP/SSE client
    RemoteServerManager.cs
  Install/
    McpInstaller.cs          — npm + Smithery installation
    SmitheryClient.cs        — Smithery registry API
  Auth/
    OAuthManager.cs          — OAuth flow
    OAuthCallbackHandler.cs
  Models/
    McpServerConfig.cs       — name, command, args, env, transport
    McpTool.cs               — Discovered tool definition
    ServerStatus.cs          — Running/stopped/error
```

### Local Server Lifecycle (stdio)

1. Read config from `.mcp_servers/local/{name}/config.json`
2. Spawn subprocess with configured command + args + env
3. JSON-RPC via stdin/stdout: `initialize`, `tools/list`, `tools/call`
4. Health monitoring, restart on crash with backoff
5. Hot reload: watch config file, restart on change (dev mode)

### Remote Server Lifecycle (HTTP)

1. Read config from `.mcp_servers/remote/{name}/config.json`
2. Connect to endpoint with optional OAuth bearer token
3. `GET /tools` for discovery, `POST /tools/{name}` for execution
4. SSE for streaming responses

### Tool Naming

MCP tools namespaced as `{server}__{tool}`. Supports both full namespace lookup and unambiguous short-name resolution.

### Installer

- npm: `npx -y {package}` to install, parse output for config
- Smithery: HTTP API search + install + configure
- Stores config in `.mcp_servers/{local|remote}/{name}/config.json`

### OAuth Flow

For Smithery-hosted remote servers:
1. `mcp_oauth_start` -> auth URL + pending state
2. User authorizes externally
3. `mcp_oauth_complete` -> exchange code for token, store in config

### MCP Management Tools (12)

`mcp_list`, `mcp_status`, `mcp_tools`, `mcp_install`, `mcp_uninstall`, `mcp_enable`, `mcp_disable`, `mcp_restart`, `mcp_hot_reload`, `mcp_refresh`, `mcp_oauth_start`, `mcp_oauth_complete`, `mcp_oauth_status`.

### DB Tables

`McpServer`, `McpToolCall`, `McpUsageMetrics`.

---

## Section 4: Sandbox Module

Docker-based code execution with per-user containers.

### Project Structure

```
MyPalClara.Modules.Sandbox/
  SandboxModule.cs
  SandboxToolSource.cs        — IToolSource (8 tools)
  Docker/
    DockerSandboxManager.cs   — Container lifecycle + execution
    ContainerPool.cs          — Per-user container tracking
    DockerClient.cs           — Docker Engine API HTTP client
  Models/
    ExecutionResult.cs
    SandboxSession.cs
```

### Container Lifecycle

1. First tool call: create container from `DOCKER_SANDBOX_IMAGE` (default: `python:3.12-slim`)
2. Container stays alive for reuse (idle timeout: 30 min)
3. Execution: `docker exec` with timeout (`DOCKER_SANDBOX_TIMEOUT`, default 900s)
4. Cleanup: stop + remove on idle or shutdown

### Docker Engine API

Via Docker socket (`/var/run/docker.sock`), HTTP client. No Docker CLI dependency.

### Sandbox Tools (8)

`execute_python`, `run_shell`, `install_package`, `read_file`, `write_file`, `list_files`, `unzip_file`, `web_search` (Tavily).

### Resource Limits

- Memory: `DOCKER_SANDBOX_MEMORY` (512MB)
- CPU: `DOCKER_SANDBOX_CPUS` (1.0)
- Network: isolated bridge, outbound only

---

## Section 5: Proactive Module (ORS)

Autonomous outreach engine with 3-state machine and 2-stage LLM assessment.

### Project Structure

```
MyPalClara.Modules.Proactive/
  ProactiveModule.cs
  Engine/
    OrsEngine.cs              — Main loop
    OrsState.cs               — WAIT / THINK / SPEAK
    OrsContext.cs              — Full context snapshot
  Context/
    TemporalContext.cs
    ConversationContext.cs
    CrossChannelContext.cs
    CadenceContext.cs
    CalendarContext.cs
  Notes/
    NoteManager.cs
    NoteTypes.cs              — observation, question, follow_up, connection
    NoteValidation.cs         — relevant, resolved, stale, contradicted
  Prompts/
    AssessmentPrompt.cs       — Stage 1: context -> situation summary
    DecisionPrompt.cs         — Stage 2: decide WAIT/THINK/SPEAK
  Delivery/
    OutreachDelivery.cs       — Send via IGatewayBridge
```

### ORS Loop

1. Gather context per active user (temporal, conversation, cross-channel, notes, cadence, history)
2. Stage 1 LLM: synthesize context into situation assessment
3. Stage 2 LLM: decide action (WAIT/THINK/SPEAK) with JSON output
4. Execute: WAIT=log, THINK=create/update note, SPEAK=deliver message

### Boundary Enforcement

- Min gap: `ORS_MIN_SPEAK_GAP_HOURS` (2.0)
- Skip users active elsewhere
- Honor timezone (no sleeping-hours messages)
- Respect opt-out (`ors_enabled` in GuildConfig)
- Note decay: `ORS_NOTE_DECAY_DAYS` (7)

### DB Tables

`ProactiveMessage`, `ProactiveNote`, `Intention`.

---

## Section 6: Email Module

IMAP monitoring with rules engine and multi-provider OAuth.

### Project Structure

```
MyPalClara.Modules.Email/
  EmailModule.cs
  Monitoring/
    ImapMonitor.cs
    EmailPoller.cs
  Rules/
    RulesEngine.cs
    Rule.cs
    RuleAction.cs             — notify, forward, label, archive
  Providers/
    IEmailProvider.cs
    ImapProvider.cs            — Direct IMAP
    GmailProvider.cs           — Gmail OAuth2
    OutlookProvider.cs         — Outlook/M365 OAuth2
  Auth/
    EmailOAuthManager.cs
    CredentialStore.cs
  Notifications/
    EmailNotifier.cs
  Models/
    EmailMessage.cs
    EmailAccount.cs
```

### Polling Flow

1. Scheduler fires poll task (default 60s)
2. Per enabled account: connect, fetch unread since last UID
3. Run rules engine on each message
4. Notify via WebSocket or execute rule actions
5. Update last_checked

### Rules Engine

JSON-stored rules per account. Conditions: `from_contains`, `from_exact`, `subject_contains`, `body_contains`, `has_attachment`. Operators: `all` (AND), `any` (OR).

### Email Tool

`check_email` — on-demand poll, returns unread summary.

---

## Section 7: Graph Module

FalkorDB entity/relationship graph with LLM triple extraction.

### Project Structure

```
MyPalClara.Modules.Graph/
  GraphModule.cs
  Client/
    FalkorDbClient.cs         — Redis protocol to FalkorDB
    GraphOperations.cs        — Entity/relationship CRUD
  Extraction/
    TripleExtractor.cs        — LLM-based extraction
    ExtractionPrompt.cs
  Cache/
    GraphCache.cs             — Redis-backed (5min search, 10min snapshot)
  Api/
    GraphApiService.cs        — Service for HTTP controllers
  Models/
    GraphEntity.cs
    GraphRelationship.cs
    GraphTriple.cs
```

### Triple Extraction

Event-driven: subscribes to `message:sent`, extracts (subject, predicate, object) triples via LLM, upserts into FalkorDB. Fire-and-forget.

### FalkorDB Client

Via `StackExchange.Redis`. Cypher queries: `GRAPH.QUERY {graph} "..."`. Same Redis connection used for cache layer.

### API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/v1/graph/entities` | GET | List with pagination, type filter |
| `/api/v1/graph/entities/{id}` | GET | Entity with relationships |
| `/api/v1/graph/relationships` | GET | List with type filter |
| `/api/v1/graph/search` | POST | Semantic search over graph |

---

## Section 8: Games Module

Minimal. LLM reasoning for game moves; state managed by Rails BFF.

```
MyPalClara.Modules.Games/
  GamesModule.cs
  GameToolSource.cs           — IToolSource
  GameReasoner.cs             — LLM move reasoning
```

Tools: `game_make_move` (reason about board state, return move), `game_analyze` (analyze state, suggest strategy). Fetches/posts game state via HTTP to Rails API.

---

## New NuGet Dependencies

| Package | Used by | Purpose |
|---------|---------|---------|
| `StackExchange.Redis` | Graph | FalkorDB + Redis cache |
| `AWSSDK.S3` | Gateway (file tools) | S3 file storage |
| `MailKit` | Email | IMAP client |
| `Docker.DotNet` | Sandbox | Docker Engine API |

---

## Project Structure Summary

```
mypalclara.net/
  src/
    MyPalClara.Api/                    # HTTP host + GraphController
    MyPalClara.Core/                   # WebSocket, router, processor
      Processing/
        LlmOrchestrator.cs            # NEW
        MessageProcessor.cs           # MODIFIED
    MyPalClara.Data/                   # EF Core (existing)
    MyPalClara.Llm/                    # LLM providers (existing)
    MyPalClara.Memory/                 # Rook (existing)
    MyPalClara.Modules.Sdk/            # Contracts (+ IToolRegistry, IToolSource)
    MyPalClara.Gateway/                # Runtime (+ Tools/)
      Tools/
        ToolRegistry.cs                # NEW
        CoreToolsRegistrar.cs          # NEW
        TerminalTools.cs / FileStorageTools.cs / ProcessManager.cs
        ChatHistoryTools.cs / SystemLogTools.cs / PersonalityTools.cs
        DiscordTools.cs
    MyPalClara.Modules.Mcp/            # Full MCP system
    MyPalClara.Modules.Sandbox/        # Docker execution
    MyPalClara.Modules.Proactive/      # ORS engine
    MyPalClara.Modules.Email/          # IMAP + rules
    MyPalClara.Modules.Graph/          # FalkorDB + extraction
    MyPalClara.Modules.Games/          # LLM game reasoning
  tests/
    MyPalClara.Gateway.Tests/          # + tool registry tests
    MyPalClara.Core.Tests/             # + orchestrator tests
    MyPalClara.Modules.Mcp.Tests/
    MyPalClara.Modules.Sandbox.Tests/
```
