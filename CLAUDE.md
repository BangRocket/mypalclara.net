# CLAUDE.md

Guidance for Claude Code working with this repository.

## Project Overview

Clara is a .NET 10 AI agent gateway. It provides an ASP.NET Core host with SignalR-based adapter communication, HTTP REST API, LLM orchestration with agentic tool calling, persistent memory, sub-agent spawning, and a heartbeat system. Platform adapters (Discord, Teams, CLI) connect to the gateway over SignalR.

## Quick Reference

```bash
# Build everything
dotnet build

# Run the gateway
dotnet run --project src/Clara.Gateway

# Run a specific adapter
dotnet run --project src/Clara.Adapters.Discord
dotnet run --project src/Clara.Adapters.Teams
dotnet run --project src/Clara.Adapters.Cli

# Run tests
dotnet test

# Docker
docker compose -f docker/docker-compose.yml up
```

## Solution Structure

```
Clara.slnx                      # .NET solution (slnx format)

src/
  Clara.Core/                   # Domain: entities, config, LLM, memory, tools, events, sessions
  Clara.Gateway/                # ASP.NET Core host: API, pipeline, queues, sandbox, services
  Clara.Adapters.Discord/       # Discord bot adapter (DSharpPlus)
  Clara.Adapters.Teams/         # Microsoft Teams adapter (Bot Framework REST API)
  Clara.Adapters.Cli/           # Interactive CLI adapter (Spectre.Console)

tests/
  Clara.Core.Tests/             # Core unit tests
  Clara.Gateway.Tests/          # Gateway integration tests

docker/
  Dockerfile                    # Multi-stage build for gateway
  docker-compose.yml            # Gateway + PostgreSQL (pgvector) + Redis + Qdrant

workspace/
  persona.md                    # Clara's persona definition
  tools.md                      # Tool usage conventions
  heartbeat.md                  # Periodic checklist for heartbeat service
  users/                        # Per-user context files
  skills/                       # Skill definitions
  memory-export/                # Memory export directory
```

## Architecture

### Data Flow

```
Adapter (Discord/Teams/CLI)
  -> SignalR -> AdapterHub.SendMessage()
  -> LaneQueueManager.EnqueueAsync() (per-session lane queuing)
  -> LaneQueueWorker dequeues
  -> MessagePipeline (middleware chain + stages)
    -> ContextBuildStage (prompt composition + memory retrieval)
    -> ToolSelectionStage (select tools for context)
    -> LlmOrchestrationStage (agentic loop: LLM -> tool calls -> LLM)
    -> ResponseRoutingStage (stream deltas back via SignalR)
  -> AdapterHub broadcasts ReceiveTextDelta/ReceiveComplete to subscribers
  -> Adapter renders response to user
```

### Key Design Decisions

- **Provider-aware DbContext** -- `OnModelCreating` detects PostgreSQL vs SQLite; jsonb and vector column types only applied for PostgreSQL; Embedding column ignored for SQLite
- **No EF Core migrations** -- schema mapping only via Fluent API
- **Strongly-typed config** -- `ClaraOptions` root with nested options for each subsystem, bound to `Clara` config section
- **Adapter pattern** -- all adapters are separate executables connecting to the gateway via SignalR; gateway is adapter-agnostic
- **Lane queue system** -- per-session message lanes prevent head-of-line blocking; single worker per lane
- **Agentic tool loop** -- `LlmOrchestrator` runs tool calls in a loop until the LLM stops requesting tools or limits are hit
- **Tool policy pipeline** -- chain of `IToolPolicy` implementations (channel, agent, sandbox) filter tool availability
- **Sub-agents** -- `SubAgentManager` spawns child LLM contexts with isolated tool sets and timeouts

## Clara.Core — Domain Layer

### Config (`Clara.Core.Config/`)

Root: `ClaraOptions` (section: `Clara`)

| Options class | Section | Key properties |
|---|---|---|
| `LlmOptions` | `Clara:Llm` | Provider, DefaultTier, AutoTierSelection, per-provider config |
| `MemoryOptions` | `Clara:Memory` | EmbeddingModel, SearchLimit, ExtractionModel, SessionIdleMinutes |
| `GatewayOptions` | `Clara:Gateway` | Host, Port, Secret, MaxToolRounds, MaxToolResultChars |
| `ToolOptions` | `Clara:Tools` | LoopDetection, DefaultPolicy, ChannelPolicies |
| `SandboxOptions` | `Clara:Sandbox` | Mode, Docker (Image, Timeout, Memory, Cpu) |
| `HeartbeatOptions` | `Clara:Heartbeat` | Enabled, IntervalMinutes, ChecklistPath |
| `SubAgentOptions` | `Clara:SubAgents` | MaxPerParent, DefaultTier, TimeoutMinutes |
| `DiscordOptions` | `Clara:Discord` | BotToken, AllowedServers, StopPhrases, MaxImages |

### Data (`Clara.Core.Data/`)

8 entities in `Clara.Core.Data.Entities/`:

| Entity | Purpose |
|---|---|
| `UserEntity` | Platform users |
| `SessionEntity` | Conversation sessions (has many Messages) |
| `MessageEntity` | Individual messages in a session |
| `MemoryEntity` | Vector memories with pgvector Embedding |
| `ProjectEntity` | Project metadata |
| `McpServerEntity` | MCP server configurations |
| `EmailAccountEntity` | Email polling accounts |
| `ToolUsageEntity` | Tool call audit log |

`ClaraDbContext` uses Fluent API mapping. Provider-aware: detects PostgreSQL (jsonb, vector columns) vs SQLite (skips Embedding).

### LLM (`Clara.Core.Llm/`)

- `ILlmProvider` -- streaming + complete interface
- `AnthropicProvider` -- Anthropic SDK-based
- `OpenAiCompatProvider` -- OpenAI, OpenRouter, NanoGPT (all OpenAI-compatible)
- `LlmProviderFactory` -- creates providers from config
- `TierClassifier` -- auto-selects model tier (high/mid/low) based on message complexity
- `LlmOrchestrator` -- agentic loop: LLM call -> parse tool calls -> execute -> feed results back -> repeat
- `ToolLoopDetector` -- prevents infinite tool loops (max identical calls, max total rounds)

### Memory (`Clara.Core.Memory/`)

- `PgVectorMemoryStore` -- EF Core-backed vector memory with similarity search
- `OpenAiEmbeddingProvider` -- text-embedding-3-small via OpenAI API
- `MemoryExtractor` -- LLM-based fact extraction from conversations
- `SessionSummarizer` -- summarizes session history
- `MarkdownMemoryView` -- renders memory context as markdown for prompt injection

### Prompt (`Clara.Core.Prompt/`)

- `PromptComposer` -- assembles system prompt from ordered sections
- Sections: `PersonaSection`, `ToolConventionsSection`, `UserContextSection`, `MemorySection`, `HeartbeatSection`, `SkillSection`
- Each section implements `IPromptSection` with priority ordering

### Sessions (`Clara.Core.Sessions/`)

- `SessionManager` -- singleton, manages active sessions with scoped DbContext
- `SessionKey` -- structured key: `clara:{agent}:{platform}:{scope}:{id}`
- `SessionTimeoutPolicy` -- configurable idle timeout

### Events (`Clara.Core.Events/`)

- `IClaraEventBus` / `ClaraEventBus` -- pub/sub event bus
- Event types: lifecycle, adapter, session, message, tool, memory, scheduler, subagent, heartbeat
- `ClaraEvent` record with Type, Timestamp, UserId, SessionKey, Platform, Data dict

### Tools (`Clara.Core.Tools/`)

- `ITool` -- Name, Description, Category, ParameterSchema, ExecuteAsync
- `IToolRegistry` / `ToolRegistry` -- tool registration and lookup
- `ToolSelector` -- selects relevant tools for a given context
- `ToolPolicyPipeline` -- chain of `IToolPolicy` filters (channel, agent, sandbox)

**Built-in tools** (23 tools in `Clara.Core.Tools.BuiltIn/`):

| Category | Tools |
|---|---|
| File | `file_read`, `file_write`, `file_list`, `file_delete` |
| GitHub | `github_list_repos`, `github_get_issues`, `github_get_pull_requests`, `github_create_issue`, `github_get_file` |
| Azure DevOps | `azdo_list_repos`, `azdo_get_work_items`, `azdo_get_pipelines` |
| Google | `google_list_files`, `google_read_sheet`, `google_create_doc` |
| Email | `email_check`, `email_read`, `email_send_alert` |
| Session | `sessions_list`, `sessions_history`, `sessions_send`, `sessions_spawn` |
| Shell | `shell_execute` |
| Web | `web_fetch`, `web_search` |

**MCP** (`Clara.Core.Tools.Mcp/`):

- `McpServerManager` -- manages MCP server lifecycle
- `McpStdioClient` / `McpHttpClient` -- stdio and HTTP MCP transport
- `McpToolWrapper` -- wraps MCP tools as `ITool` for the registry
- `McpRegistryAdapter` -- discovers tools from MCP registry
- `McpInstaller` -- installs MCP servers
- `McpOAuthHandler` -- OAuth flow for MCP servers

### Sub-Agents (`Clara.Core.SubAgents/`)

- `ISubAgentManager` / `SubAgentManager` -- spawns child LLM contexts
- `SubAgentRequest` / `SubAgentResult` -- request/result types
- Limits: max per parent, default tier, timeout

## Clara.Gateway — Host Layer

### API Controllers (`Clara.Gateway.Api/`)

| Controller | Routes |
|---|---|
| `HealthController` | `GET /api/v1/health` |
| `SessionsController` | `GET/POST /api/v1/sessions` |
| `MemoriesController` | `GET/POST /api/v1/memories` |
| `AdminController` | `GET /api/v1/admin/*` |
| `OAuthController` | MCP OAuth callback routes |

ASP.NET Core health checks at `GET /health` (includes DbContext check).

### SignalR Hubs (`Clara.Gateway.Hubs/`)

- `AdapterHub` -- adapter communication (SendMessage, Subscribe, Unsubscribe, Authenticate)
- `MonitorHub` -- real-time monitoring
- Client interface: `IAdapterClient` (ReceiveTextDelta, ReceiveToolStatus, ReceiveComplete, ReceiveError)

### Pipeline (`Clara.Gateway.Pipeline/`)

Middleware chain:
1. `LoggingMiddleware` -- request/response logging
2. `StopPhraseMiddleware` -- checks for stop phrases
3. `RateLimitMiddleware` -- rate limiting

Processing stages:
1. `ContextBuildStage` -- builds prompt context with memory
2. `ToolSelectionStage` -- selects available tools
3. `LlmOrchestrationStage` -- runs LLM agentic loop
4. `ResponseRoutingStage` -- routes responses to adapters

### Queues (`Clara.Gateway.Queues/`)

- `LaneQueueManager` -- per-session message lanes
- `LaneQueueWorker` -- `IHostedService` that processes queued messages
- `QueueMetrics` -- enqueue/dequeue counts
- `SessionMessage` -- queued message record

### Sandbox (`Clara.Gateway.Sandbox/`)

- `ISandboxProvider` / `DockerSandbox` -- Docker container management via Docker.DotNet
- `SandboxManager` -- container lifecycle management

### Services (`Clara.Gateway.Services/`)

- `HeartbeatService` -- periodic LLM-driven checklist execution
- `SchedulerService` -- background task scheduler (cron, interval, one-shot)
- `SessionCleanupService` -- cleans up idle sessions
- `MemoryConsolidationService` -- periodic memory consolidation
- `CronParser` -- 5-field cron expression parser

### Hooks (`Clara.Gateway.Hooks/`)

- `HookRegistry` -- loads hook definitions from YAML
- `HookExecutor` -- spawns shell processes for hook execution
- `HookDefinition` -- hook config (event, command, timeout)

## Adapters

### Clara.Adapters.Discord

Standalone executable. Connects to Discord via DSharpPlus and to the gateway via SignalR.

- `DiscordAdapter` -- main adapter, wires Discord events to gateway
- `DiscordGatewayClient` -- SignalR client for gateway communication
- `DiscordMessageMapper` -- maps Discord messages to session keys
- `DiscordResponseSender` -- sends chunked responses back to Discord
- `DiscordSlashCommands` -- Discord slash command registration
- `DiscordImageHandler` -- handles image attachments

### Clara.Adapters.Teams

Standalone executable. Receives Bot Framework activities via HTTP POST and connects to the gateway via SignalR.

- `TeamsAdapter` -- main adapter, handles activities and sends replies via Bot Framework REST API
- `TeamsGatewayClient` -- SignalR client for gateway communication
- `TeamsMessageMapper` -- maps Teams activities to session keys, extracts content/users
- Uses direct HTTP to Bot Framework (no Bot Framework SDK dependency)
- Endpoint: `POST /api/messages` receives activities from Teams
- Acquires OAuth tokens from `login.microsoftonline.com` for reply authentication

### Clara.Adapters.Cli

Standalone executable. Interactive REPL for local development and testing.

- `CliAdapter` -- REPL loop, sends messages, waits for streaming responses
- `CliGatewayClient` -- SignalR client for gateway communication
- `CliRenderer` -- Spectre.Console-based terminal rendering (figlet banner, streaming text, tool status)
- Commands: `/help`, `/clear`, `/quit`

## Configuration

All configuration is in `src/Clara.Gateway/appsettings.json` under the `Clara` section. Environment variables override with `__` delimiter (e.g., `Clara__Gateway__Host`).

### Required Environment Variables

- `OPENAI_API_KEY` -- always required for embeddings
- Provider-specific API key (e.g., `ANTHROPIC_API_KEY`)

### Adapter Environment Variables

**Discord:** `DISCORD_BOT_TOKEN`, `CLARA_GATEWAY_URL`, `CLARA_GATEWAY_SECRET`
**Teams:** `TEAMS_APP_ID`, `TEAMS_APP_PASSWORD`, `TEAMS_BOT_NAME`, `CLARA_GATEWAY_URL`
**CLI:** `CLARA_GATEWAY_URL`, `CLARA_GATEWAY_SECRET`, `CLARA_CLI_USER`

## Testing

```bash
dotnet test                                  # All tests (279 passing)
dotnet test tests/Clara.Core.Tests          # Core tests (232)
dotnet test tests/Clara.Gateway.Tests       # Gateway tests (47)
```

Tests use SQLite in-memory databases. The DbContext is provider-aware so Embedding columns are ignored under SQLite.

## NuGet Packages

### Clara.Core

- `Microsoft.EntityFrameworkCore` (10.x)
- `Npgsql.EntityFrameworkCore.PostgreSQL` (10.x preview)
- `Microsoft.EntityFrameworkCore.Sqlite` (10.x)
- `Pgvector.EntityFrameworkCore` (0.3.0)
- `Microsoft.Extensions.Http`, `DependencyInjection.Abstractions`, `Logging.Abstractions`, `Options` (10.x)
- `Anthropic` (0.x) -- Anthropic SDK
- `OpenAI` (2.x) -- OpenAI SDK
- `YamlDotNet` (16.x)

### Clara.Gateway

- `Serilog.AspNetCore` (9.x)
- `Docker.DotNet` (3.x)
- `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` (10.x)

### Clara.Adapters.Discord

- `DSharpPlus` (5.x)
- `Microsoft.AspNetCore.SignalR.Client` (10.x)
- `Serilog.Extensions.Hosting` (10.x)
- `Serilog.Sinks.Console` (6.x)

### Clara.Adapters.Teams

- `Microsoft.AspNetCore.SignalR.Client` (10.x)
- `Serilog.Extensions.Hosting` (10.x)
- `Serilog.Sinks.Console` (6.x)

### Clara.Adapters.Cli

- `Spectre.Console` (0.x)
- `Microsoft.AspNetCore.SignalR.Client` (10.x)

## DI Registration

Core services registered via `AddClaraCore(configuration)` in `ServiceCollectionExtensions.cs`.
Built-in tools registered via `RegisterBuiltInTools()` after building the service provider.
Gateway services registered individually in `Program.cs`.

## Docker

```bash
# Local development (from docker/ directory)
docker compose -f docker/docker-compose.yml up

# Standalone build
docker build -f docker/Dockerfile -t clara-gateway .
```

Services: Gateway, PostgreSQL (pgvector), Redis, Qdrant.

## CI/CD

GitHub Actions workflow at `.github/workflows/ci.yml`:
- Triggers on push/PR to main
- .NET 10 preview SDK
- Build, test, publish, upload artifact
