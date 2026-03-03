# MyPalClara .NET Migration Plan

**Version:** 1.0  
**Date:** March 2026  
**Authors:** Joshua / Clara (via Claude)  
**Status:** Planning  

---

## 1. Executive Summary

This document lays out a phased plan to rewrite MyPalClara in .NET, incorporating architectural lessons from the existing Python implementation and key patterns from OpenClaw that have proven effective at scale. The rewrite is not a line-by-line port — it's a ground-up rethink that preserves what works (Gateway-as-brain, multi-adapter architecture, MCP integration) while adopting patterns that solve known pain points (autonomous feedback loops, opaque memory, context bloat, monolithic tool injection).

### 1.1 Why .NET?

- Strong typing catches entire categories of bugs that plague dynamic Python codebases at Clara's complexity level
- `async/await` and `Channel<T>` are first-class — no more asyncio footguns
- Native AOT compilation option for fast startup and low memory footprint
- Entity Framework Core + Npgsql provide mature PostgreSQL + pgvector support
- gRPC and SignalR are built into ASP.NET Core — no third-party WebSocket libraries
- Single deployment artifact (self-contained publish) simplifies Docker images
- C#'s interface/DI system maps naturally to the adapter/gateway/core layering

### 1.2 What We're Keeping

| Concept | Status | Notes |
|---------|--------|-------|
| Gateway-as-brain architecture | Keep | Validated by both Clara and OpenClaw |
| Multi-adapter pattern | Keep | Discord, Teams, CLI, future desktop |
| MCP plugin system | Keep | Rewrite MCP client in C# |
| Model tier system (high/mid/low) | Keep | Proven useful |
| CalVer versioning | Keep | YYYY.WW.N format |
| PostgreSQL + pgvector | Keep | Consolidate to single DB |
| Docker sandbox execution | Keep | Add Incus option |

### 1.3 What We're Changing (OpenClaw-Informed)

| Pattern | Current Clara | New Design | OpenClaw Influence |
|---------|--------------|------------|-------------------|
| Tool injection | All 40+ tools every message | Selective injection via classifier | OpenClaw discovers skills at runtime, injects only relevant ones |
| Memory visibility | Opaque mem0 vectors | Hybrid: pgvector + human-readable Markdown export | OpenClaw's MEMORY.md is editable, inspectable, Git-backable |
| Prompt composition | Monolithic system prompt | Composable files: persona.md, tools.md, user.md | OpenClaw's AGENTS.md + SOUL.md + TOOLS.md separation |
| Session keys | Simple IDs | Structured keys encoding routing context | OpenClaw's `agent:main:<platform>:<channel>:<user>` pattern |
| Execution model | Single pipeline | Lane queues with serial-default, explicit-parallel | OpenClaw's "Default Serial, Explicit Parallel" philosophy |
| Sub-agents | Not supported | spawn_subagent for parallel background tasks | OpenClaw's sessions_spawn with cheaper model assignment |
| Tool loop detection | None (ORS removed) | Built-in loop detection with configurable limits | OpenClaw's tools.loopDetection config |
| Heartbeat/proactive | Deprecated (proactive_engine.py) | Bounded heartbeat with user-controlled checklist | OpenClaw's HEARTBEAT.md approach |
| Tool policies | Admin-only MCP permissions | Layered allow/deny per-agent, per-channel, per-provider | OpenClaw's cascading tool policy pipeline |

---

## 2. Solution Architecture

### 2.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        MyPalClara.NET Architecture                          │
└─────────────────────────────────────────────────────────────────────────────┘

                        ┌─────────────────────────┐
                        │      Entry Points        │
                        └─────────────────────────┘
                                    │
     ┌──────────────────────────────┼──────────────────────────────┐
     │                              │                              │
     ▼                              ▼                              ▼
┌──────────────┐          ┌──────────────────┐          ┌──────────────┐
│   Discord    │          │     Teams        │          │     CLI      │
│   Adapter    │          │    Adapter       │          │   Adapter    │
│  (DSharpPlus)│          │(Bot Framework)   │          │  (Terminal)  │
└──────┬───────┘          └────────┬─────────┘          └──────┬───────┘
       │                           │                           │
       └───────────────────────────┼───────────────────────────┘
                                   │
                          SignalR / WebSocket
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         GATEWAY (ASP.NET Core Host)                         │
│                                                                             │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐   │
│  │   Router    │  │  Lane Queue  │  │   Session    │  │   Scheduler   │   │
│  │             │──│  Manager     │──│   Manager    │  │  (Cron/Hook)  │   │
│  │ (Inbound   │  │ (Serial by   │  │ (Structured  │  │               │   │
│  │  dispatch)  │  │  default)    │  │  Keys)       │  │               │   │
│  └─────────────┘  └──────┬───────┘  └──────────────┘  └───────────────┘   │
│                          │                                                 │
│                          ▼                                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     MESSAGE PIPELINE                                │   │
│  │                                                                     │   │
│  │  ┌──────────┐   ┌──────────┐   ┌──────────────┐   ┌────────────┐  │   │
│  │  │ Context  │──▶│  Tool    │──▶│     LLM      │──▶│  Response  │  │   │
│  │  │ Builder  │   │ Selector │   │ Orchestrator │   │  Router    │  │   │
│  │  │          │   │(Classify)│   │ (Streaming + │   │            │  │   │
│  │  │• Memory  │   │          │   │  Tool Loop)  │   │• Stream    │  │   │
│  │  │• Session │   │• Inject  │   │              │   │• Batch     │  │   │
│  │  │• Persona │   │  only    │   │• Intercept   │   │• SubAgent  │  │   │
│  │  │• User ctx│   │  relevant│   │  tool calls  │   │  announce  │  │   │
│  │  └──────────┘   │  tools   │   │• Feed results│   └────────────┘  │   │
│  │                 └──────────┘   │  back in     │                    │   │
│  │                                │• Loop detect │                    │   │
│  │                                └──────────────┘                    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐                      │
│  │  SubAgent    │  │   Tool       │  │  Heartbeat   │                      │
│  │  Manager     │  │  Executor    │  │  Service     │                      │
│  │ (Spawn/Track │  │ (Native+MCP │  │ (Checklist-  │                      │
│  │  background  │  │  +Sandbox)   │  │  driven)     │                      │
│  │  tasks)      │  │              │  │              │                      │
│  └──────────────┘  └──────────────┘  └──────────────┘                      │
└─────────────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          CORE (Clara.Core library)                          │
│                                                                             │
│  ┌─────────────────┐  ┌──────────────────┐  ┌──────────────────────────┐   │
│  │  LLM Providers  │  │  Memory System   │  │   Prompt Composition     │   │
│  │                 │  │                  │  │                          │   │
│  │ • Anthropic     │  │ • MemoryStore    │  │ • PersonaProvider        │   │
│  │ • OpenRouter    │  │   (pgvector)     │  │   (persona.md)           │   │
│  │ • OpenAI-compat │  │ • MemoryView     │  │ • ToolConventions        │   │
│  │ • NanoGPT       │  │   (Markdown      │  │   (tools.md)             │   │
│  │                 │  │    export/import) │  │ • UserContext            │   │
│  │ Streaming via   │  │ • SessionContext  │  │   (user.md per-user)     │   │
│  │ IAsyncEnumerable│  │ • GraphMemory    │  │ • SkillInjector          │   │
│  └─────────────────┘  │   (optional)     │  │   (runtime discovery)    │   │
│                       └──────────────────┘  └──────────────────────────┘   │
│                                                                             │
│  ┌─────────────────┐  ┌──────────────────┐  ┌──────────────────────────┐   │
│  │  Tool System    │  │   MCP Client     │  │   Configuration          │   │
│  │                 │  │                  │  │                          │   │
│  │ • IToolRegistry │  │ • stdio/HTTP     │  │ • appsettings.json       │   │
│  │ • ToolPolicy    │  │ • OAuth          │  │ • Environment vars       │   │
│  │   Pipeline      │  │ • Namespaced     │  │ • Per-user overrides     │   │
│  │ • ToolCategory  │  │   tools          │  │ • Hot reload             │   │
│  │   Classification│  │ • Install/       │  │                          │   │
│  │ • Loop Detection│  │   Manage         │  │                          │   │
│  └─────────────────┘  └──────────────────┘  └──────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           DATA LAYER                                        │
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │                    PostgreSQL (Single Instance)                       │   │
│  │                                                                      │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐   │   │
│  │  │  Relational  │  │   pgvector   │  │      JSONB Storage       │   │   │
│  │  │              │  │              │  │                          │   │   │
│  │  │ • Sessions   │  │ • Embeddings │  │ • Tool configs           │   │   │
│  │  │ • Messages   │  │ • Semantic   │  │ • MCP server state       │   │   │
│  │  │ • Users      │  │   search     │  │ • User preferences       │   │   │
│  │  │ • Projects   │  │ • Memory     │  │ • Session snapshots      │   │   │
│  │  │ • MCP Servers│  │   vectors    │  │                          │   │   │
│  │  └──────────────┘  └──────────────┘  └──────────────────────────┘   │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌──────────────────┐  ┌──────────────────────────────────────────────┐     │
│  │  Redis (Optional)│  │  Workspace (Filesystem)                     │     │
│  │                  │  │                                              │     │
│  │ • Rate limiting  │  │ • persona.md, tools.md, user.md             │     │
│  │ • Pub/sub events │  │ • HEARTBEAT.md                              │     │
│  │ • Hot session    │  │ • memory-export/ (human-readable snapshots) │     │
│  │   cache          │  │ • skills/<name>/SKILL.md                    │     │
│  └──────────────────┘  └──────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 Structured Session Keys

Adopt OpenClaw's pattern of encoding routing context into the session key itself:

```
clara:<agentId>:<platform>:<scope>:<identifier>

Examples:
  clara:main:discord:dm:123456789          # Discord DM with user
  clara:main:discord:channel:987654321     # Discord channel
  clara:main:teams:chat:abc-def-123        # Teams 1:1 chat
  clara:main:cli:local:default             # CLI session
  clara:sub:main:discord:dm:123456789:task-7a3b  # Sub-agent spawned from a Discord DM
```

The session key drives:
- **Workspace resolution** — which persona/tools files to load
- **Tool policy lookup** — which tools are available for this context
- **Lane queue routing** — which serial queue this message enters
- **Session file paths** — where transcripts and state are stored
- **Sandbox decisions** — whether to run tools in a Docker sandbox

### 2.3 Lane Queue System

Inspired by OpenClaw's "Default Serial, Explicit Parallel" philosophy — and directly informed by the ORS feedback loop disaster.

```csharp
// Each session key gets its own bounded channel (serial lane)
public class LaneQueueManager
{
    private readonly ConcurrentDictionary<string, Channel<SessionMessage>> _lanes = new();

    public async Task EnqueueAsync(string sessionKey, SessionMessage message)
    {
        var lane = _lanes.GetOrAdd(sessionKey, _ =>
            Channel.CreateBounded<SessionMessage>(new BoundedChannelOptions(50)
            {
                FullMode = BoundedChannelFullMode.Wait
            }));

        await lane.Writer.WriteAsync(message);
    }
}
```

Rules:
- Messages within a lane process **serially** — no interleaving, no race conditions
- Sub-agent spawns create a **new lane** with their own session key
- Heartbeat tasks run in a **dedicated lane** (`clara:heartbeat:*`)
- Cross-lane communication happens via a typed event bus (not LLM-driven)
- Each lane has a **max concurrent tool calls** limit (configurable, default 1)

---

## 3. Solution Structure

```
MyPalClara/
├── MyPalClara.sln
│
├── src/
│   ├── Clara.Core/                         # Core library (no host dependency)
│   │   ├── Clara.Core.csproj
│   │   │
│   │   ├── Llm/                            # LLM provider abstraction
│   │   │   ├── ILlmProvider.cs             # interface: streaming + non-streaming
│   │   │   ├── ILlmProviderFactory.cs      # resolves provider by name/tier
│   │   │   ├── LlmRequest.cs               # model, messages, tools, temperature
│   │   │   ├── LlmResponse.cs              # content, tool_calls, usage
│   │   │   ├── LlmStreamChunk.cs           # delta content, tool call fragments
│   │   │   ├── ModelTier.cs                 # enum: High, Mid, Low
│   │   │   ├── TierClassifier.cs           # auto-classify message complexity
│   │   │   ├── Providers/
│   │   │   │   ├── AnthropicProvider.cs     # native Anthropic SDK
│   │   │   │   ├── OpenRouterProvider.cs    # OpenRouter via OpenAI SDK
│   │   │   │   ├── OpenAiCompatProvider.cs  # generic OpenAI-compatible
│   │   │   │   └── NanoGptProvider.cs       # NanoGPT
│   │   │   └── ToolCalling/
│   │   │       ├── ToolCallParser.cs        # extract tool calls from stream
│   │   │       ├── ToolCallResult.cs        # result to feed back into LLM
│   │   │       └── ToolLoopDetector.cs      # detect repeated/circular calls
│   │   │
│   │   ├── Memory/                          # Memory system
│   │   │   ├── IMemoryStore.cs              # interface: store, search, delete
│   │   │   ├── IMemoryView.cs              # interface: export to Markdown, import
│   │   │   ├── MemoryEntry.cs              # user_id, content, embedding, metadata
│   │   │   ├── MemorySearchResult.cs       # entry + relevance score
│   │   │   ├── SessionContext.cs           # recent messages + prev session snapshot
│   │   │   ├── PgVectorMemoryStore.cs      # PostgreSQL + pgvector implementation
│   │   │   ├── MarkdownMemoryView.cs       # export/import human-readable Markdown
│   │   │   ├── MemoryExtractor.cs          # LLM-based fact extraction from conversations
│   │   │   ├── EmotionalContext.cs         # emotional continuity tracking
│   │   │   └── SessionSummarizer.cs        # LLM-generated session summaries
│   │   │
│   │   ├── Prompt/                          # Composable prompt system
│   │   │   ├── IPromptSection.cs           # interface: async content provider
│   │   │   ├── PromptComposer.cs           # assembles sections into system prompt
│   │   │   ├── PersonaSection.cs           # loads persona.md
│   │   │   ├── ToolConventionsSection.cs   # loads tools.md
│   │   │   ├── UserContextSection.cs       # loads user-specific context
│   │   │   ├── MemorySection.cs            # injects relevant memories
│   │   │   ├── SkillSection.cs             # runtime-discovered skill injection
│   │   │   └── HeartbeatSection.cs         # checklist items (when applicable)
│   │   │
│   │   ├── Tools/                           # Tool system
│   │   │   ├── IToolRegistry.cs            # interface: register, resolve, list
│   │   │   ├── ITool.cs                    # interface: name, description, schema, execute
│   │   │   ├── ToolDefinition.cs           # JSON schema for tool parameters
│   │   │   ├── ToolResult.cs               # success/failure + content
│   │   │   ├── ToolCategory.cs             # enum: FileSystem, Web, GitHub, Code, etc.
│   │   │   ├── ToolSelector.cs             # classify message → select relevant categories
│   │   │   ├── ToolPolicy/
│   │   │   │   ├── IToolPolicy.cs          # interface: allow/deny evaluation
│   │   │   │   ├── ToolPolicyPipeline.cs   # cascading policy evaluation
│   │   │   │   ├── AgentToolPolicy.cs      # per-agent allow/deny
│   │   │   │   ├── ChannelToolPolicy.cs    # per-channel restrictions
│   │   │   │   └── SandboxToolPolicy.cs    # sandbox-specific restrictions
│   │   │   ├── BuiltIn/
│   │   │   │   ├── FileTools.cs            # read, write, list, delete
│   │   │   │   ├── ShellTool.cs            # execute shell commands
│   │   │   │   ├── WebSearchTool.cs        # Tavily integration
│   │   │   │   ├── WebFetchTool.cs         # URL content extraction
│   │   │   │   ├── GitHubTools.cs          # full GitHub API integration
│   │   │   │   ├── AzureDevOpsTools.cs     # repos, pipelines, work items
│   │   │   │   ├── GoogleWorkspaceTools.cs # Sheets, Drive, Docs, Calendar
│   │   │   │   ├── EmailTools.cs           # monitoring and alerts
│   │   │   │   ├── MemoryTools.cs          # memory view/edit/export
│   │   │   │   └── SessionTools.cs         # list, history, send, spawn
│   │   │   └── Mcp/
│   │   │       ├── IMcpClient.cs           # interface: connect, list tools, call
│   │   │       ├── McpStdioClient.cs       # stdio transport
│   │   │       ├── McpHttpClient.cs        # HTTP/SSE transport
│   │   │       ├── McpServerManager.cs     # lifecycle management
│   │   │       ├── McpInstaller.cs         # install from Smithery/npm/GitHub
│   │   │       ├── McpOAuthHandler.cs      # OAuth for hosted servers
│   │   │       └── McpRegistryAdapter.cs   # bridge MCP tools → IToolRegistry
│   │   │
│   │   ├── Sessions/                        # Session management
│   │   │   ├── ISessionManager.cs          # interface: get, create, timeout
│   │   │   ├── SessionKey.cs               # structured key parser/builder
│   │   │   ├── Session.cs                  # state, messages, metadata
│   │   │   ├── SessionManager.cs           # PostgreSQL-backed implementation
│   │   │   └── SessionTimeoutPolicy.cs    # configurable idle timeout
│   │   │
│   │   ├── SubAgents/                       # Sub-agent spawning
│   │   │   ├── ISubAgentManager.cs         # interface: spawn, track, announce
│   │   │   ├── SubAgentRequest.cs          # task, model tier, parent session
│   │   │   ├── SubAgentResult.cs           # outcome + content
│   │   │   └── SubAgentManager.cs          # spawn into new lane, announce back
│   │   │
│   │   ├── Config/                          # Configuration
│   │   │   ├── ClaraOptions.cs             # strongly-typed root config
│   │   │   ├── LlmOptions.cs              # per-provider model/tier config
│   │   │   ├── MemoryOptions.cs           # pgvector, graph, embedding config
│   │   │   ├── SandboxOptions.cs          # Docker/Incus/Remote config
│   │   │   ├── ToolPolicyOptions.cs       # allow/deny list config
│   │   │   └── HeartbeatOptions.cs        # interval, checklist path
│   │   │
│   │   ├── Events/                          # Internal event bus
│   │   │   ├── IClaraEventBus.cs          # pub/sub interface
│   │   │   ├── ClaraEvent.cs              # base event type
│   │   │   ├── SessionEvents.cs           # start, end, timeout
│   │   │   ├── MessageEvents.cs           # received, sent, cancelled
│   │   │   ├── ToolEvents.cs              # start, end, error
│   │   │   └── AdapterEvents.cs           # connected, disconnected
│   │   │
│   │   └── Data/                            # Data access
│   │       ├── ClaraDbContext.cs           # EF Core context
│   │       ├── Entities/
│   │       │   ├── UserEntity.cs
│   │       │   ├── SessionEntity.cs
│   │       │   ├── MessageEntity.cs
│   │       │   ├── MemoryEntity.cs         # with vector column
│   │       │   ├── ProjectEntity.cs
│   │       │   ├── McpServerEntity.cs
│   │       │   ├── EmailAccountEntity.cs
│   │       │   └── ToolUsageEntity.cs      # for analytics
│   │       └── Migrations/
│   │
│   ├── Clara.Gateway/                       # ASP.NET Core host
│   │   ├── Clara.Gateway.csproj
│   │   ├── Program.cs                      # host builder + DI registration
│   │   ├── appsettings.json
│   │   │
│   │   ├── Pipeline/                        # Message processing pipeline
│   │   │   ├── IMessagePipeline.cs
│   │   │   ├── MessagePipeline.cs          # orchestrates the stages
│   │   │   ├── Stages/
│   │   │   │   ├── ContextBuildStage.cs    # memory + session + persona
│   │   │   │   ├── ToolSelectionStage.cs   # classify + inject relevant tools
│   │   │   │   ├── LlmOrchestrationStage.cs # streaming + tool loop
│   │   │   │   └── ResponseRoutingStage.cs # back to adapter
│   │   │   └── Middleware/
│   │   │       ├── RateLimitMiddleware.cs
│   │   │       ├── StopPhraseMiddleware.cs # "Clara stop", "nevermind"
│   │   │       └── LoggingMiddleware.cs
│   │   │
│   │   ├── Queues/                          # Lane queue system
│   │   │   ├── LaneQueueManager.cs         # per-session serial queues
│   │   │   ├── LaneQueueWorker.cs          # background service processing lanes
│   │   │   └── QueueMetrics.cs             # depth, wait time tracking
│   │   │
│   │   ├── Hubs/                            # SignalR hubs
│   │   │   ├── AdapterHub.cs              # adapter connections
│   │   │   └── MonitorHub.cs              # dashboard/monitoring
│   │   │
│   │   ├── Services/                        # Background services
│   │   │   ├── HeartbeatService.cs         # periodic checklist evaluation
│   │   │   ├── SessionCleanupService.cs    # timeout idle sessions
│   │   │   ├── MemoryConsolidationService.cs # periodic memory maintenance
│   │   │   └── SchedulerService.cs         # cron/interval tasks
│   │   │
│   │   ├── Hooks/                           # Event hooks
│   │   │   ├── HookRegistry.cs
│   │   │   ├── HookExecutor.cs
│   │   │   └── hooks.yaml                  # hook definitions
│   │   │
│   │   ├── Sandbox/                         # Code execution
│   │   │   ├── ISandboxProvider.cs
│   │   │   ├── DockerSandbox.cs
│   │   │   ├── IncusSandbox.cs
│   │   │   ├── RemoteSandbox.cs
│   │   │   └── SandboxManager.cs           # auto-select backend
│   │   │
│   │   └── Api/                             # REST endpoints
│   │       ├── HealthController.cs
│   │       ├── OAuthController.cs          # Google, MCP OAuth callbacks
│   │       └── AdminController.cs          # management API
│   │
│   ├── Clara.Adapters.Discord/              # Discord adapter
│   │   ├── Clara.Adapters.Discord.csproj
│   │   ├── DiscordAdapter.cs               # DSharpPlus bot
│   │   ├── DiscordMessageMapper.cs         # Discord → ClaraMessage
│   │   ├── DiscordResponseSender.cs        # streaming, embeds, reactions
│   │   ├── DiscordImageHandler.cs          # image resize + base64
│   │   ├── DiscordSlashCommands.cs         # /clara, /mcp commands
│   │   └── DiscordGatewayClient.cs         # SignalR client to gateway
│   │
│   ├── Clara.Adapters.Teams/               # Teams adapter
│   │   ├── Clara.Adapters.Teams.csproj
│   │   ├── TeamsAdapter.cs
│   │   ├── TeamsMessageMapper.cs
│   │   └── TeamsGatewayClient.cs
│   │
│   ├── Clara.Adapters.Cli/                 # CLI adapter
│   │   ├── Clara.Adapters.Cli.csproj
│   │   ├── CliAdapter.cs
│   │   ├── CliRenderer.cs                  # Markdown rendering in terminal
│   │   └── CliGatewayClient.cs
│   │
│   └── Clara.Adapters.Desktop/             # Desktop widget (future)
│       ├── Clara.Adapters.Desktop.csproj
│       └── (Tauri + React frontend, .NET SignalR client)
│
├── tests/
│   ├── Clara.Core.Tests/
│   │   ├── Llm/
│   │   │   ├── TierClassifierTests.cs
│   │   │   ├── ToolCallParserTests.cs
│   │   │   └── ToolLoopDetectorTests.cs
│   │   ├── Memory/
│   │   │   ├── PgVectorMemoryStoreTests.cs
│   │   │   ├── MarkdownMemoryViewTests.cs
│   │   │   └── MemoryExtractorTests.cs
│   │   ├── Tools/
│   │   │   ├── ToolSelectorTests.cs
│   │   │   ├── ToolPolicyPipelineTests.cs
│   │   │   └── McpClientTests.cs
│   │   ├── Sessions/
│   │   │   ├── SessionKeyTests.cs
│   │   │   └── SessionManagerTests.cs
│   │   └── Prompt/
│   │       └── PromptComposerTests.cs
│   │
│   ├── Clara.Gateway.Tests/
│   │   ├── Pipeline/
│   │   │   └── MessagePipelineTests.cs
│   │   └── Queues/
│   │       └── LaneQueueManagerTests.cs
│   │
│   └── Clara.Integration.Tests/            # Integration tests (requires Docker)
│       ├── LlmProviderIntegrationTests.cs
│       ├── MemoryStoreIntegrationTests.cs
│       └── SandboxIntegrationTests.cs
│
├── workspace/                               # Clara's workspace (human-editable)
│   ├── persona.md                           # Clara's personality
│   ├── tools.md                             # Tool usage conventions
│   ├── heartbeat.md                         # Periodic checklist
│   ├── users/
│   │   └── {user_id}/
│   │       └── user.md                      # Per-user context
│   ├── skills/                              # Installable skills
│   │   └── {skill_name}/
│   │       └── SKILL.md
│   └── memory-export/                       # Human-readable memory snapshots
│       └── {user_id}/
│           └── memories.md
│
├── docker/
│   ├── Dockerfile                           # Multi-stage build
│   ├── Dockerfile.sandbox                   # Sandbox image
│   └── docker-compose.yml                   # Gateway + PostgreSQL + Redis
│
├── scripts/
│   ├── migrate-from-python.sh              # Data migration helper
│   └── bump-version.sh                     # CalVer versioning
│
├── .github/
│   └── workflows/
│       ├── ci.yml                           # Build + test
│       └── promote-to-main.yml             # Stage → main deployment
│
├── CLAUDE.md                                # Development guide for Claude Code
├── README.md
├── VERSION
└── LICENSE
```

---

## 4. Key Interfaces

These are the foundational contracts that everything else depends on. Getting these right means everything plugs together cleanly.

### 4.1 LLM Provider

```csharp
public interface ILlmProvider
{
    string Name { get; }
    
    Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken ct = default);
    
    IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        LlmRequest request,
        CancellationToken ct = default);
}

public record LlmRequest(
    string Model,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<ToolDefinition>? Tools = null,
    float Temperature = 0.7f,
    int? MaxTokens = null);

public record LlmMessage(
    LlmRole Role,
    IReadOnlyList<LlmContent> Content);

// Content can be text, image, tool_call, tool_result
public abstract record LlmContent;
public record TextContent(string Text) : LlmContent;
public record ImageContent(string Base64, string MediaType) : LlmContent;
public record ToolCallContent(string Id, string Name, JsonElement Arguments) : LlmContent;
public record ToolResultContent(string ToolCallId, string Content, bool IsError = false) : LlmContent;
```

### 4.2 Memory

```csharp
public interface IMemoryStore
{
    Task StoreAsync(string userId, string content, MemoryMetadata? metadata = null, CancellationToken ct = default);
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(string userId, string query, int limit = 10, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryEntry>> GetAllAsync(string userId, CancellationToken ct = default);
    Task DeleteAsync(string userId, Guid memoryId, CancellationToken ct = default);
}

public interface IMemoryView
{
    /// Export all memories for a user to human-readable Markdown
    Task<string> ExportToMarkdownAsync(string userId, CancellationToken ct = default);
    
    /// Import memories from Markdown (merge, not replace)
    Task ImportFromMarkdownAsync(string userId, string markdown, CancellationToken ct = default);
    
    /// Get a single memory's content for display
    Task<string?> GetReadableAsync(string userId, Guid memoryId, CancellationToken ct = default);
}
```

### 4.3 Tools

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    ToolCategory Category { get; }
    JsonElement ParameterSchema { get; }
    
    Task<ToolResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken ct = default);
}

public record ToolExecutionContext(
    string UserId,
    string SessionKey,
    string Platform,       // "discord", "teams", "cli"
    bool IsSandboxed,
    string? WorkspaceDir);

public enum ToolCategory
{
    FileSystem, Shell, Web, GitHub, AzureDevOps,
    GoogleWorkspace, Email, Memory, Session, 
    CodeExecution, Communication, Mcp
}
```

### 4.4 Tool Policy Pipeline

```csharp
public interface IToolPolicy
{
    int Priority { get; }  // lower = evaluated first
    ToolPolicyDecision Evaluate(string toolName, ToolExecutionContext context);
}

public enum ToolPolicyDecision { Allow, Deny, Abstain }

public class ToolPolicyPipeline
{
    private readonly IEnumerable<IToolPolicy> _policies;
    
    public bool IsAllowed(string toolName, ToolExecutionContext context)
    {
        foreach (var policy in _policies.OrderBy(p => p.Priority))
        {
            var decision = policy.Evaluate(toolName, context);
            if (decision == ToolPolicyDecision.Deny) return false;
            if (decision == ToolPolicyDecision.Allow) return true;
        }
        return true; // default allow if no policy cares
    }
}
```

### 4.5 Session Key

```csharp
public record SessionKey
{
    public string AgentId { get; init; } = "main";
    public string Platform { get; init; }           // "discord", "teams", "cli"
    public string Scope { get; init; }              // "dm", "channel", "group", "local"
    public string Identifier { get; init; }         // platform-specific ID
    public string? SubTaskId { get; init; }         // for sub-agents
    
    public override string ToString() =>
        SubTaskId is not null
            ? $"clara:sub:{AgentId}:{Platform}:{Scope}:{Identifier}:{SubTaskId}"
            : $"clara:{AgentId}:{Platform}:{Scope}:{Identifier}";
    
    public static SessionKey Parse(string key) { /* ... */ }
    
    public bool IsSubAgent => SubTaskId is not null;
    public string ParentKey => $"clara:{AgentId}:{Platform}:{Scope}:{Identifier}";
    public string LaneKey => IsSubAgent ? ToString() : ParentKey; // sub-agents get own lane
}
```

---

## 5. Phased Implementation Plan

### Phase 0: Foundation (Week 1-2)

**Goal:** Solution scaffold, CI/CD, database, and basic DI wiring.

**Tasks:**
1. Create solution structure with all projects (empty implementations)
2. Set up GitHub Actions CI: build → test → Docker image
3. EF Core `ClaraDbContext` with entities: User, Session, Message, Memory (with vector), Project, McpServer
4. Initial migration + PostgreSQL + pgvector Docker Compose
5. `appsettings.json` with strongly-typed options binding
6. CalVer versioning script (port from Python)
7. CLAUDE.md for the .NET repo

**Deliverables:**
- `dotnet build` succeeds
- `dotnet test` runs (empty tests pass)
- `docker-compose up` brings up PostgreSQL with pgvector
- EF migrations create schema
- CI pipeline green

**Key decisions to lock in:**
- .NET 9 (latest LTS at time of writing)
- DSharpPlus for Discord (vs Discord.NET — DSharpPlus has better async support)
- Npgsql.EntityFrameworkCore.PostgreSQL + pgvector extension
- SignalR for gateway ↔ adapter communication (vs raw WebSocket)
- xUnit + NSubstitute for testing

---

### Phase 1: Core Intelligence (Week 3-5)

**Goal:** Clara can think and respond via a single LLM provider, with basic memory.

**Tasks:**

**1a. LLM Provider Layer**
- `ILlmProvider` interface with `CompleteAsync` and `StreamAsync`
- `AnthropicProvider` — native Anthropic SDK with streaming
- `OpenRouterProvider` — OpenAI SDK pointed at OpenRouter
- `OpenAiCompatProvider` — generic OpenAI-compatible (for clewdr, etc.)
- `ILlmProviderFactory` resolving provider by config + tier
- `ModelTier` enum and tier-specific model resolution
- `TierClassifier` — classify message complexity using low-tier model
- Unit tests for all providers with mocked HTTP responses

**1b. Memory System**
- `PgVectorMemoryStore` — store/search/delete memories via pgvector
- Embedding generation via OpenAI text-embedding-3-small
- `MemoryExtractor` — LLM-based extraction of facts from conversations
- `SessionContext` — load recent N messages + previous session snapshot
- `SessionSummarizer` — generate summary on session timeout
- `MarkdownMemoryView` — export all memories to readable Markdown
- `EmotionalContext` — basic sentiment tracking across messages

**1c. Prompt Composition**
- `PromptComposer` assembling `IPromptSection` implementations
- `PersonaSection` loading `workspace/persona.md`
- `ToolConventionsSection` loading `workspace/tools.md`
- `UserContextSection` loading `workspace/users/{userId}/user.md`
- `MemorySection` injecting top-K relevant memories from search
- Port Clara's persona from the Python system prompt to `persona.md`

**1d. Session Management**
- `SessionKey` parser/builder
- `SessionManager` — create, load, timeout, summarize
- 30-minute idle timeout (configurable)
- Session state persistence to PostgreSQL

**Deliverables:**
- Can send a message via integration test, get a response from Anthropic/OpenRouter
- Memories persist across test runs
- `workspace/persona.md` contains Clara's personality
- Session summarization works on timeout
- Memory export produces readable Markdown

**Validation:** Write a test that simulates 5 messages, verifies memory extraction, times out the session, verifies summary, starts a new session, and verifies the previous session context is included.

---

### Phase 2: Tool System + Agentic Loop (Week 6-8)

**Goal:** Clara can use tools, with streaming tool interception and loop detection.

**Tasks:**

**2a. Tool Infrastructure**
- `IToolRegistry` with register/resolve/list by category
- `ITool` base interface with JSON schema validation
- `ToolPolicyPipeline` with cascading allow/deny
- `ToolSelector` — classify message to determine which tool categories to inject
- `ToolLoopDetector` — detect N identical calls, circular patterns, runaway iteration

**2b. LLM Orchestrator (The Agentic Loop)**

This is the heart of the system. The orchestrator:
1. Sends the LLM request with selected tools
2. Streams the response
3. Intercepts tool calls mid-stream
4. Executes the tool
5. Feeds the result back into the LLM as a new message
6. Repeats until the LLM produces a final text response or limits are hit

```csharp
public class LlmOrchestrator
{
    private readonly ILlmProvider _provider;
    private readonly IToolRegistry _tools;
    private readonly ToolLoopDetector _loopDetector;
    private readonly int _maxToolRounds;

    public async IAsyncEnumerable<OrchestratorEvent> RunAsync(
        LlmRequest initialRequest,
        ToolExecutionContext toolContext,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = initialRequest.Messages.ToList();
        var round = 0;

        while (round < _maxToolRounds)
        {
            var request = initialRequest with { Messages = messages };
            var toolCalls = new List<ToolCallContent>();
            
            await foreach (var chunk in _provider.StreamAsync(request, ct))
            {
                if (chunk.IsTextDelta)
                    yield return new TextDelta(chunk.Text);
                
                if (chunk.IsToolCall)
                    toolCalls.Add(chunk.ToolCall);
            }
            
            if (toolCalls.Count == 0)
                yield break; // Final text response, done
            
            // Execute tools
            foreach (var call in toolCalls)
            {
                if (_loopDetector.IsLoop(call))
                {
                    yield return new LoopDetected(call.Name, round);
                    yield break;
                }
                
                yield return new ToolStarted(call.Name, call.Arguments);
                var result = await _tools.ExecuteAsync(call, toolContext, ct);
                yield return new ToolCompleted(call.Name, result);
                
                messages.Add(/* assistant message with tool_call */);
                messages.Add(/* tool result message */);
            }
            
            round++;
        }
        
        yield return new MaxRoundsReached(_maxToolRounds);
    }
}
```

**2c. Built-In Tools (Priority Order)**
1. `ShellTool` — execute commands (sandboxed or host)
2. `FileTools` — read, write, list, delete
3. `WebSearchTool` — Tavily integration
4. `WebFetchTool` — URL content extraction
5. `MemoryTools` — view, search, export, import memories
6. `SessionTools` — list sessions, get history

**2d. Sandbox System**
- `ISandboxProvider` interface
- `DockerSandbox` — port from Python
- `SandboxManager` — auto-select based on config
- Sandbox policy integration with tool policies

**Deliverables:**
- Clara can execute multi-step tool chains (e.g., "search the web for X, then summarize what you find")
- Tool loop detector prevents runaway execution
- Selective tool injection reduces token usage measurably (A/B test: all tools vs. selected)
- Sandbox executes Python/Bash code safely

**Validation:** Test case where Clara needs 3+ tool calls to complete a task. Verify streaming output interleaves text and tool status. Verify loop detector stops after N identical calls.

---

### Phase 3: Gateway + Discord Adapter (Week 9-11)

**Goal:** Clara is accessible via Discord through the gateway.

**Tasks:**

**3a. Gateway Infrastructure**
- ASP.NET Core host with SignalR hub for adapter connections
- `LaneQueueManager` with per-session serial queues
- `LaneQueueWorker` as `BackgroundService` draining queues
- `MessagePipeline` wiring together the stages from Phase 2
- Stop phrase middleware ("Clara stop", "nevermind")
- Rate limiting middleware
- Event bus (`IClaraEventBus`) for internal pub/sub

**3b. Discord Adapter**
- `DiscordAdapter` using DSharpPlus
- `DiscordMessageMapper` — Discord message → `ClaraMessage`
- `DiscordResponseSender` — streaming responses, reactions, embeds
- `DiscordImageHandler` — resize, base64, batching
- `DiscordSlashCommands` — `/clara mode`, `/mcp` commands
- Active/Mention/Off channel modes
- Active mode message batching (the "catching up on N messages" pattern)
- SignalR client connecting to gateway

**3c. Message Flow**
```
Discord message arrives
  → DiscordAdapter receives
  → DiscordMessageMapper normalizes
  → SignalR → AdapterHub.SendMessage()
  → Router resolves SessionKey
  → LaneQueueManager.EnqueueAsync(sessionKey, message)
  → LaneQueueWorker picks up (serial)
  → MessagePipeline.ProcessAsync():
      → ContextBuildStage (memory, session, persona, user.md)
      → ToolSelectionStage (classify → inject relevant tools)
      → LlmOrchestrationStage (stream + tool loop)
      → ResponseRoutingStage (back to adapter via SignalR)
  → DiscordResponseSender streams to Discord
```

**3d. Hooks + Scheduler**
- Hook registry loading from `hooks.yaml`
- Hook executor (shell commands, environment variables)
- Scheduler service (cron, interval, one-shot tasks)

**Deliverables:**
- Discord bot responds to messages through the gateway
- Streaming responses show typing indicator then content
- Stop phrases interrupt processing
- Channel modes (active/mention/off) work
- Active mode batches rapid messages

**Validation:** Full end-to-end test from Discord message to response, including tool usage.

---

### Phase 4: MCP + Extended Tools (Week 12-14)

**Goal:** Full tool ecosystem including MCP plugins and all integrations.

**Tasks:**

**4a. MCP Client**
- `McpStdioClient` — spawn process, communicate via stdin/stdout
- `McpHttpClient` — HTTP/SSE transport for hosted servers
- `McpServerManager` — lifecycle (start, stop, restart, health check)
- `McpInstaller` — Smithery, npm, GitHub, Docker, local path
- `McpOAuthHandler` — OAuth flow for hosted Smithery servers
- `McpRegistryAdapter` — register MCP tools into `IToolRegistry` with namespacing
- Slash commands for MCP management

**4b. Extended Built-In Tools**
- GitHub tools (full API: repos, issues, PRs, workflows, files, gists)
- Azure DevOps tools (repos, pipelines, work items)
- Google Workspace tools (Sheets, Drive, Docs, Calendar) with OAuth
- Email monitoring tools
- Claude Code integration tool (delegate to Claude Code agent)

**4c. Tool Description Generation**
- Port the tool status description system (LLM-generated descriptions during execution)
- Configurable tier for description generation

**Deliverables:**
- `@Clara install the MCP server smithery:exa` works
- All existing Python tools have .NET equivalents
- MCP tools appear in tool registry with namespaced names
- OAuth flow for hosted servers works end-to-end

---

### Phase 5: Sub-Agents + Heartbeat (Week 15-17)

**Goal:** Clara can spawn background tasks and proactively check in.

**Tasks:**

**5a. Sub-Agent System**
- `SubAgentManager` — spawn a new lane with its own session
- Sub-agent uses configurable (typically cheaper) model tier
- Parent session gets a "sub-agent spawned" notification
- Sub-agent announces results back to parent session when complete
- Configurable `maxChildrenPerAgent` (default 5)
- Sub-agent inherits tool policies from parent but with restricted set

**5b. Session Tools for Cross-Session Communication**
- `sessions_list` — discover active sessions
- `sessions_history` — get transcript from another session
- `sessions_send` — send a message to another session
- `sessions_spawn` — spawn a sub-agent (used by the LLM)

**5c. Heartbeat System**
- `HeartbeatService` as `BackgroundService`
- Reads `workspace/heartbeat.md` as a checklist
- On each heartbeat (configurable interval, default 30min):
  - Evaluates each checklist item via LLM
  - If action needed, sends a message to the appropriate session
  - If nothing needed, silently logs HEARTBEAT_OK
- User can edit `heartbeat.md` to control what gets checked

Example `heartbeat.md`:
```markdown
# Clara Heartbeat Checklist

- [ ] Check if any GitHub PRs need review on BangRocket/mypalclara
- [ ] Check if any emails from recruiters arrived in the last hour
- [ ] Remind Joshua about daily standup if it's 9:45 AM on a weekday
```

**Deliverables:**
- Clara can spawn a sub-agent: "Hey Clara, research X in the background while we talk about Y"
- Sub-agent results appear in the conversation when done
- Heartbeat evaluates checklist and proactively notifies when needed
- No autonomous feedback loops (heartbeat is bounded and checklist-driven)

---

### Phase 6: Teams + CLI Adapters + Polish (Week 18-20)

**Goal:** Multi-platform support and production readiness.

**Tasks:**

**6a. Teams Adapter**
- Port from Python Bot Framework SDK to .NET Bot Framework SDK
- Conversation history via Microsoft Graph API
- File uploads to OneDrive
- Adaptive Cards for rich responses
- RSC permissions for scoped access

**6b. CLI Adapter**
- Interactive terminal chat with Markdown rendering
- Gateway connection via SignalR
- Shell completions for common commands

**6c. Data Migration**
- Script to migrate from Python PostgreSQL schema to .NET schema
- mem0 memory export → pgvector import
- Session history migration
- MCP server configuration migration

**6d. Production Hardening**
- Health check endpoints
- Structured logging (Serilog)
- Metrics (Prometheus-compatible)
- Docker multi-stage build (optimized image size)
- Docker Compose for full stack (Gateway + PostgreSQL + Redis)
- Railway deployment config
- Database backup service port

**6e. Documentation**
- Updated TDD (this document, finalized)
- Updated README
- Updated CLAUDE.md for .NET development
- Wiki updates

**Deliverables:**
- Clara works on Discord, Teams, and CLI simultaneously through the same gateway
- Data migration from Python version completes without data loss
- Docker image under 100MB (self-contained publish)
- Production deployment on Railway

---

## 6. Data Migration Strategy

### 6.1 Schema Mapping

| Python (SQLAlchemy) | .NET (EF Core) | Notes |
|---------------------|----------------|-------|
| `Project` | `ProjectEntity` | Direct port |
| `Session` | `SessionEntity` | Add structured session key |
| `Message` | `MessageEntity` | Add tool_calls JSONB column |
| `ChannelSummary` | `SessionSummaryEntity` | Rename for clarity |
| `MCPServer` | `McpServerEntity` | Direct port |
| `EmailAccount` | `EmailAccountEntity` | Direct port |
| `EmailRule` | `EmailRuleEntity` | Direct port |
| mem0 vectors | `MemoryEntity` + pgvector | New unified table |

### 6.2 Migration Script

```bash
# 1. Export from Python PostgreSQL
pg_dump -Fc $OLD_DATABASE_URL > clara_python_backup.dump

# 2. Run .NET EF migrations on new database
dotnet ef database update --project src/Clara.Gateway

# 3. Run migration script
dotnet run --project scripts/MigratePythonData \
    --source $OLD_DATABASE_URL \
    --target $NEW_DATABASE_URL \
    --mem0-source $OLD_MEM0_DATABASE_URL

# 4. Verify
dotnet run --project scripts/MigratePythonData --verify
```

### 6.3 Memory Migration

The mem0 → pgvector migration requires re-embedding if the embedding model changes, or direct vector copy if using the same model (text-embedding-3-small):

1. Export mem0 memories with embeddings from old pgvector database
2. Map to new `MemoryEntity` schema (add structured metadata)
3. Insert with existing embeddings (no re-embedding needed if same model)
4. Generate initial Markdown export per user to `workspace/memory-export/`
5. Verify semantic search returns same results for test queries

---

## 7. Configuration

### 7.1 appsettings.json Structure

```jsonc
{
  "Clara": {
    // LLM
    "Llm": {
      "Provider": "anthropic",  // anthropic | openrouter | openai | nanogpt
      "DefaultTier": "mid",
      "AutoTierSelection": true,
      "Anthropic": {
        "ApiKey": "${ANTHROPIC_API_KEY}",
        "BaseUrl": null,  // for proxies like clewdr
        "Models": {
          "High": "claude-opus-4-5",
          "Mid": "claude-sonnet-4-5",
          "Low": "claude-haiku-4-5"
        }
      },
      "OpenRouter": { /* ... */ },
      "OpenAi": { /* ... */ },
      "NanoGpt": { /* ... */ }
    },

    // Memory
    "Memory": {
      "EmbeddingModel": "text-embedding-3-small",
      "EmbeddingApiKey": "${OPENAI_API_KEY}",
      "SearchLimit": 10,
      "ExtractionModel": "gpt-4o-mini",
      "SessionIdleMinutes": 30,
      "EnableGraphMemory": false
    },

    // Gateway
    "Gateway": {
      "Host": "127.0.0.1",
      "Port": 18789,
      "Secret": "${CLARA_GATEWAY_SECRET}",
      "MaxToolRounds": 10,
      "MaxToolResultChars": 50000
    },

    // Tools
    "Tools": {
      "LoopDetection": {
        "MaxIdenticalCalls": 3,
        "MaxTotalRounds": 10
      },
      "DescriptionTier": "low",
      "DescriptionMaxWords": 20,
      "DefaultPolicy": {
        "Allow": ["*"],
        "Deny": []
      },
      "ChannelPolicies": {
        "discord:channel:*": {
          "Deny": ["shell_execute"]  // no shell in public channels
        }
      }
    },

    // Sandbox
    "Sandbox": {
      "Mode": "auto",  // docker | incus | incus-vm | remote | auto
      "Docker": {
        "Image": "python:3.12-slim",
        "TimeoutSeconds": 900,
        "Memory": "512m",
        "Cpu": 1.0
      }
    },

    // Heartbeat
    "Heartbeat": {
      "Enabled": false,
      "IntervalMinutes": 30,
      "ChecklistPath": "workspace/heartbeat.md"
    },

    // SubAgents
    "SubAgents": {
      "MaxPerParent": 5,
      "DefaultTier": "low",
      "TimeoutMinutes": 10
    },

    // Discord
    "Discord": {
      "BotToken": "${DISCORD_BOT_TOKEN}",
      "AllowedServers": [],
      "MaxImages": 1,
      "MaxImageDimension": 1568,
      "StopPhrases": ["clara stop", "stop clara", "nevermind", "never mind"]
    },

    // Teams
    "Teams": {
      "AppId": "${TEAMS_APP_ID}",
      "AppPassword": "${TEAMS_APP_PASSWORD}"
    },

    // Integrations
    "GitHub": { "Token": "${GITHUB_TOKEN}" },
    "AzureDevOps": { "Org": "${AZURE_DEVOPS_ORG}", "Pat": "${AZURE_DEVOPS_PAT}" },
    "Google": {
      "ClientId": "${GOOGLE_CLIENT_ID}",
      "ClientSecret": "${GOOGLE_CLIENT_SECRET}",
      "RedirectUri": "${GOOGLE_REDIRECT_URI}"
    },
    "Tavily": { "ApiKey": "${TAVILY_API_KEY}" }
  },

  "ConnectionStrings": {
    "Clara": "${DATABASE_URL}"
  }
}
```

---

## 8. Key Design Decisions

### 8.1 Single Database (PostgreSQL Only)

**Decision:** Consolidate FalkonDB + Redis + PostgreSQL into PostgreSQL only, with optional Redis for caching.

**Rationale:** 
- pgvector handles all vector search needs (replaces Qdrant/FalkonDB)
- JSONB columns handle semi-structured data (tool configs, session snapshots)
- EF Core provides a single, well-tested data access layer
- Redis is optional and only used for truly ephemeral data (rate limiting, pub/sub)
- One backup strategy, one failure mode, one connection pool

### 8.2 SignalR over Raw WebSocket

**Decision:** Use SignalR for gateway ↔ adapter communication.

**Rationale:**
- Built into ASP.NET Core, no third-party dependencies
- Automatic reconnection with backoff
- Strongly-typed hub methods (compile-time safety)
- Built-in group/user targeting for routing
- Scales with Azure SignalR Service if ever needed

### 8.3 IAsyncEnumerable for Streaming

**Decision:** Use `IAsyncEnumerable<T>` as the streaming primitive throughout.

**Rationale:**
- Native C# pattern, no library dependency
- Composes naturally with `await foreach` and LINQ
- Cancellation via `CancellationToken` is built in
- Maps cleanly to SignalR streaming methods

### 8.4 Workspace Files over Database-Only Config

**Decision:** Store persona, tool conventions, user context, and heartbeat checklist as Markdown files on disk, not only in the database.

**Rationale (OpenClaw-informed):**
- Human-editable without any tooling
- Git-backable for version history
- Debuggable — you can `cat persona.md` and see exactly what the LLM gets
- Composable — swap personas by changing a file
- Memory export gives users visibility into what Clara remembers
- Keeps the database for structured/relational data, files for semi-structured text

### 8.5 No Autonomous Feedback Loops

**Decision:** All cognitive activity remains anchored to either (a) user messages or (b) bounded heartbeat checks. No autonomous rumination, no ORS-style self-triggered loops.

**Rationale (learned the hard way):**
- ORS caused hallucination amplification, stale context surfacing, and runaway cycles
- OpenClaw's heartbeat system proves that proactive behavior can work safely when it's bounded and checklist-driven
- Sub-agents are user-initiated (or heartbeat-initiated), not self-spawning
- The agentic loop is bounded by `MaxToolRounds` — it cannot decide to keep going indefinitely

---

## 9. Risk Mitigation

| Risk | Mitigation |
|------|------------|
| .NET MCP SDK maturity | MCP protocol is simple (JSON-RPC over stdio/HTTP). If no mature C# SDK exists, implement a thin client — the protocol surface is small. |
| DSharpPlus stability | DSharpPlus is actively maintained and used in production. Fallback: Discord.NET. The adapter pattern means swapping is localized. |
| pgvector search quality vs. Qdrant | pgvector with HNSW indexes performs comparably for the scale Clara operates at (thousands of memories, not millions). Test with real memory data during migration. |
| Memory migration data loss | Run migration in dry-run mode first. Verify search results match between old and new systems before switching. Keep old database as read-only backup for 30 days. |
| Tool selection classifier accuracy | Start with a simple keyword/category matcher before investing in LLM-based classification. Measure token savings vs. missed tool availability. Fall back to "inject all" if classifier confidence is low. |
| Sub-agent cost creep | Hard limit on concurrent sub-agents per parent (default 5). Sub-agents default to cheapest tier. Timeout after 10 minutes. Log all sub-agent costs. |

---

## 10. Success Criteria

| Criterion | Measurement |
|-----------|-------------|
| Feature parity with Python version | All existing tools, adapters, and capabilities work in .NET |
| Response latency | Time-to-first-token ≤ Python version (streaming) |
| Token efficiency | ≥15% reduction in prompt tokens via selective tool injection |
| Memory accuracy | Semantic search returns same top-5 results as mem0 for test queries |
| Reliability | Gateway uptime ≥99.5% over 30-day production run |
| Data migration | Zero data loss from Python → .NET migration |
| Developer experience | `dotnet build` < 10s, `dotnet test` < 30s, Docker image < 100MB |

---

## 11. Timeline Summary

| Phase | Weeks | Goal |
|-------|-------|------|
| **0: Foundation** | 1-2 | Solution scaffold, CI, database |
| **1: Core Intelligence** | 3-5 | LLM, memory, prompts, sessions |
| **2: Tool System** | 6-8 | Tools, agentic loop, sandbox |
| **3: Gateway + Discord** | 9-11 | Full Discord adapter through gateway |
| **4: MCP + Extended Tools** | 12-14 | MCP plugins, all integrations |
| **5: Sub-Agents + Heartbeat** | 15-17 | Background tasks, proactive check-ins |
| **6: Multi-Platform + Polish** | 18-20 | Teams, CLI, migration, production |

**Total estimated duration: ~20 weeks** (assuming part-time solo development, adjust for availability)

---

## 12. References

- [MyPalClara Python repo](https://github.com/BangRocket/mypalclara)
- [OpenClaw Architecture](https://docs.openclaw.ai/)
- [OpenClaw DeepWiki](https://deepwiki.com/openclaw/openclaw)
- [MCP Protocol Specification](https://modelcontextprotocol.io)
- [pgvector for .NET](https://github.com/pgvector/pgvector-dotnet)
- [DSharpPlus](https://github.com/DSharpPlus/DSharpPlus)
- [ASP.NET Core SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/)

---

*Clara is a unified assistant accessible through multiple interfaces. The Gateway is her brain, adapters are her ears and mouth, memory gives her continuity, and workspace files give her transparency. The .NET rewrite preserves what works while adopting patterns that have proven effective at scale.*
