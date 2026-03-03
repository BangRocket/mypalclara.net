# Clara

A .NET 10 AI agent gateway with multi-platform adapters, LLM orchestration, tool execution, and persistent memory.

## Requirements

- .NET 10 SDK
- PostgreSQL with pgvector (production) or SQLite (development)
- OpenAI API key (embeddings)
- LLM provider API key (Anthropic, OpenRouter, etc.)

## Getting Started

```bash
# Build
dotnet build

# Set required environment variables
export OPENAI_API_KEY=your-key
export ANTHROPIC_API_KEY=your-key

# Run the gateway
dotnet run --project src/Clara.Gateway

# Run tests
dotnet test
```

The gateway starts on port **18789** by default (configurable via `Clara:Gateway:Port` in appsettings.json).

## Docker

```bash
# Start gateway + PostgreSQL (pgvector) + Redis
docker compose -f docker/docker-compose.yml up
```

## Architecture

```
Adapters                          Gateway                              Core Library
+-----------+                     +----------------------------------+
| Discord   |---+                 |  Clara.Gateway (ASP.NET Core)    |   Clara.Core
| (DSharpPlus)  |   SignalR       |                                  |
+-----------+   +---------------->|  AdapterHub (SignalR)             |   +------------------+
                |                 |       |                          |   | LLM Providers    |
+-----------+   |                 |  LaneQueueManager                |   |  Anthropic        |
| Teams     |---+                 |  (per-session serial queues)     |   |  OpenAI-compat    |
| (Bot API) |                     |       |                          |   +------------------+
+-----------+                     |  MessagePipeline                 |   | Memory System    |
                                  |   -> StopPhrase middleware       |   |  pgvector store   |
+-----------+                     |   -> RateLimit middleware        |   |  Markdown export  |
| CLI       |---+                 |   -> ContextBuild stage          |   +------------------+
| (Spectre) |   |                 |   -> ToolSelection stage         |   | Tool System      |
+-----------+   |                 |   -> LlmOrchestration stage      |   |  23+ built-in     |
                +---------------->|   -> ResponseRouting stage       |   |  MCP client       |
                                  |                                  |   |  Policy pipeline  |
                                  |  Background Services:            |   +------------------+
                                  |   SessionCleanup, Heartbeat,     |   | Sessions         |
                                  |   Scheduler, MemoryConsolidation |   |  Structured keys  |
                                  +----------------------------------+   +------------------+
```

### Projects

| Project | Description |
|---------|-------------|
| `Clara.Core` | Domain library: LLM providers, memory, tools, sessions, prompts, events, EF Core entities |
| `Clara.Gateway` | ASP.NET Core host: message pipeline, lane queues, SignalR hubs, REST API, background services |
| `Clara.Adapters.Discord` | Discord bot via DSharpPlus 5.0, connects to gateway via SignalR |
| `Clara.Adapters.Teams` | Teams bot via Bot Framework REST API, connects to gateway via SignalR |
| `Clara.Adapters.Cli` | Interactive terminal REPL via Spectre.Console, connects to gateway via SignalR |

### Data Flow

```
Adapter receives message
  -> SignalR -> AdapterHub.SendMessage()
  -> LaneQueueManager enqueues (per-session serial queue)
  -> LaneQueueWorker picks up message
  -> MessagePipeline.ProcessAsync():
      1. Middleware: stop phrases, rate limiting, logging
      2. ContextBuildStage: load session, compose prompt (persona + tools + user + memories)
      3. ToolSelectionStage: classify message, inject relevant tools only
      4. LlmOrchestrationStage: agentic loop (LLM -> tool calls -> results -> repeat)
      5. ResponseRoutingStage: stream back to adapter via SignalR
```

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | /health | Health check (ASP.NET Core health checks) |
| GET | /api/v1/health | Gateway health |
| GET | /api/v1/sessions | List sessions (paginated) |
| GET | /api/v1/sessions/:id | Get session with messages |
| PUT | /api/v1/sessions/:id | Rename session |
| PATCH | /api/v1/sessions/:id/archive | Archive session |
| DELETE | /api/v1/sessions/:id | Delete session |
| GET | /api/v1/memories | List memories |
| POST | /api/v1/memories/search | Semantic search |
| GET | /api/v1/memories/stats | Memory statistics |
| GET | /api/v1/admin/users | List all users |
| GET | /api/v1/oauth/callback | OAuth callback |

SignalR hubs: `/hubs/adapter` (adapters), `/hubs/monitor` (dashboard)

## Tool System

23+ built-in tools across 10 categories, plus dynamic MCP tools:

| Category | Tools |
|----------|-------|
| FileSystem | file_read, file_write, file_list, file_delete |
| Shell | shell_execute |
| Web | web_search (Tavily), web_fetch |
| Memory | memory_search, memory_list, memory_export |
| Session | sessions_list, sessions_history, sessions_send, sessions_spawn |
| GitHub | github_list_repos, github_get_issues, github_get_pull_requests, github_create_issue, github_get_file |
| AzureDevOps | azdo_list_repos, azdo_get_work_items, azdo_get_pipelines |
| GoogleWorkspace | google_list_files, google_read_sheet, google_create_doc |
| Email | email_check, email_read, email_send_alert |
| MCP | Dynamic tools from installed MCP servers (namespaced as `mcp_{server}_{tool}`) |

### Tool Policies

Cascading allow/deny pipeline: Agent policies -> Channel policies -> Sandbox policies. Configure per-channel restrictions in appsettings.json:

```json
{
  "Clara": {
    "Tools": {
      "ChannelPolicies": {
        "discord:channel:*": {
          "Deny": ["shell_execute"]
        }
      }
    }
  }
}
```

### MCP Integration

Install and manage MCP servers at runtime:

- **stdio transport**: spawns a child process, JSON-RPC over stdin/stdout
- **HTTP transport**: connects to remote MCP servers
- **Installer**: supports Smithery (`smithery:package`), npm (`npm:package`), and local paths

## Session Keys

Structured keys encoding routing context:

```
clara:main:discord:dm:123456789        # Discord DM
clara:main:discord:channel:987654321   # Discord channel
clara:main:teams:chat:abc-def-123      # Teams chat
clara:main:cli:dm:username             # CLI session
clara:sub:main:discord:dm:123:task-7a  # Sub-agent
```

## Sub-Agents

Clara can spawn background sub-agents for parallel tasks:
- Configurable model tier (defaults to Low for cost efficiency)
- Max 5 sub-agents per parent session
- Results announced back to parent session on completion
- Timeout enforcement (default 10 minutes)

## Heartbeat

Periodic checklist evaluation via `workspace/heartbeat.md`:

```markdown
- [ ] Check if any GitHub PRs need review
- [ ] Remind about daily standup if 9:45 AM on a weekday
```

The heartbeat service evaluates each item via LLM and publishes events when action is needed. Bounded and checklist-driven — no autonomous feedback loops.

## Hooks

Shell commands triggered by gateway events. Define in `hooks/hooks.yaml`:

```yaml
hooks:
  - name: log-sessions
    event: session:start
    command: echo "Session started for ${CLARA_USER_ID}"
    timeout: 30
    enabled: true
```

Hook commands receive event data as `CLARA_*` environment variables.

## Scheduler

Background task scheduler supporting interval, cron, and one-shot tasks. Define in `scheduler.yaml`:

```yaml
tasks:
  - name: cleanup-sessions
    type: interval
    interval: 3600
    command: ./scripts/cleanup.sh
    timeout: 300
    enabled: true

  - name: daily-summary
    type: cron
    cron: "0 9 * * *"
    command: ./scripts/daily_summary.sh
```

## Workspace

Human-editable files that control Clara's behavior:

```
workspace/
  persona.md         # Clara's personality and communication style
  tools.md           # Tool usage conventions
  heartbeat.md       # Periodic checklist items
  users/{id}/user.md # Per-user context
  skills/            # Installable skills
  memory-export/     # Human-readable memory snapshots
```

## Configuration

All configuration under the `Clara` section in `appsettings.json`. Key sections:

- **Llm**: provider selection, API keys, model tiers (High/Mid/Low)
- **Memory**: embedding model, search limits, extraction
- **Gateway**: host, port, secret, tool limits
- **Tools**: loop detection, description tier, channel policies
- **Sandbox**: Docker container settings
- **Heartbeat**: interval, checklist path
- **SubAgents**: max per parent, default tier, timeout
- **Discord**: bot token, allowed servers, stop phrases

Environment variables override config values.

## License

Private -- Clara project.
