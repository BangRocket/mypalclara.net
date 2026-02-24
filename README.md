# MyPalClara

.NET reimplementation of the MyPalClara gateway -- WebSocket server for platform adapters and HTTP API for the web UI.

## Requirements

- .NET 10 SDK
- Qdrant (vector database)
- PostgreSQL (production) or SQLite (development)
- OpenAI API key (embeddings)
- LLM provider API key (Anthropic, OpenRouter, etc.)

## Getting Started

```bash
# Clone and build
git clone <repo-url>
cd MyPalClara
dotnet build

# Set required environment variables
export OPENAI_API_KEY=your-key
export LLM_PROVIDER=anthropic
export ANTHROPIC_API_KEY=your-key

# Run
dotnet run --project src/MyPalClara.Api
```

The gateway starts:
- HTTP API on port **18790**
- WebSocket server on port **18789**

## Docker

```bash
# Copy and configure environment
cp .env.example .env
# Edit .env with your API keys

# Start with dependencies
docker-compose up
```

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | /api/v1/health | Health check |
| GET | /api/v1/sessions | List sessions |
| GET | /api/v1/sessions/:id | Get session with messages |
| PUT | /api/v1/sessions/:id | Rename session |
| PATCH | /api/v1/sessions/:id/archive | Archive session |
| DELETE | /api/v1/sessions/:id | Delete session |
| GET | /api/v1/users/me | Current user profile |
| PUT | /api/v1/users/me | Update profile |
| GET | /api/v1/admin/users | List all users (admin) |
| GET | /api/v1/memories | List memories |
| POST | /api/v1/memories/search | Semantic search |
| GET | /api/v1/memories/stats | Memory statistics |
| GET | /api/v1/intentions | List intentions |

## Architecture

```
+-------------+     WebSocket      +------------------+
|   Discord   |<------------------>|                  |
|   Adapter   |                    |   MyPalClara   |
+-------------+                    |                  |
                                   |  +------------+  |
+-------------+     WebSocket      |  |  Gateway   |  |
|   Teams     |<------------------>|  |  Server    |  |
|   Adapter   |                    |  +-----+------+  |
+-------------+                    |        |         |
                                   |  +-----v------+  |
+-------------+     HTTP API       |  |  Message   |  |
|   Web UI    |<------------------>|  |  Router    |  |
|   (React)   |                    |  +-----+------+  |
+-------------+                    |        |         |
                                   |  +-----v------+  |
                                   |  |  Message   |  |
                                   |  |  Processor |  |
                                   |  +--+-----+---+  |
                                   |     |     |      |
                                   |  +--v--+ +v---+  |
                                   |  | LLM | |Rook|  |
                                   |  +-----+ +----+  |
                                   +------------------+
```

## Modules

The gateway supports pluggable modules that run alongside platform adapters. Modules are discovered from the `modules/` directory at startup.

### Enabling/Disabling Modules

In `appsettings.json`:

```json
{
  "Modules": {
    "Directory": "./modules",
    "mcp": true,
    "sandbox": false,
    "proactive": false,
    "email": false,
    "graph": false,
    "games": false
  }
}
```

Set a module to `true` to enable it, `false` to disable. Disabled modules are not started even if their DLL is present.

### Available Modules

| Module | Purpose |
|--------|---------|
| mcp | MCP server lifecycle, tool discovery, tool execution |
| sandbox | Docker/Incus code execution |
| proactive | ORS assessment loop, proactive outreach |
| email | Account polling, rule evaluation, alerts |
| graph | FalkorDB entity/relationship graph |
| games | AI move decisions for turn-based games |

## Hooks

Shell scripts triggered by gateway events. Define hooks in `hooks/hooks.yaml`:

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

Events include: `gateway:startup`, `gateway:shutdown`, `session:start`, `session:end`, `message:received`, `message:sent`, `tool:start`, `tool:end`, and more.

Hook commands receive event data as `CLARA_*` environment variables (`CLARA_EVENT_TYPE`, `CLARA_USER_ID`, `CLARA_CHANNEL_ID`, `CLARA_EVENT_DATA`, etc.).

## Scheduler

Background task scheduler supporting interval, cron, and one-shot tasks. Define tasks in `scheduler.yaml`:

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

Cron expressions use standard 5-field format (minute hour day_of_month month day_of_week) with support for `*`, `*/N`, `N-M`, and `N,M`.

## License

Private -- MyPalClara project.
