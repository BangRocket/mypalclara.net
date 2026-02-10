# MyPalClara.NET

.NET 10 CLI port of [MyPalClara](https://github.com/heidornj/mypalclara), an AI companion with persistent memory.

## Architecture

Two projects:

- **Clara.Core** — Class library containing LLM providers, MCP server management, and the full memory subsystem
- **Clara.Cli** — Console application with a Spectre.Console REPL

### Memory Subsystem

- **Vector store** (pgvector) — Semantic search over memories
- **FSRS-6** — Spaced repetition scoring for memory relevance decay
- **Graph store** (FalkorDB) — Entity/relationship extraction and traversal
- **Emotional context** — VADER-style sentiment tracking with emotional arc computation
- **Topic recurrence** — LLM-based topic extraction with pattern detection
- **Contradiction detection** — 5-layer detection (negation, antonym, temporal, numeric, LLM semantic)
- **Smart ingest** — Deduplication, contradiction resolution, and supersession pipeline
- **Redis cache** — Embedding and search result caching

### LLM Providers

- **Anthropic** (primary) — Streaming chat with tool use via the Messages API
- **Rook** (auxiliary) — OpenAI-compatible provider for fact extraction, topic extraction, and graph entity extraction

### MCP Integration

Connects to the same MCP servers as the Python version via the official [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) .NET SDK. Reads configs from `.mcp_servers/local/{name}/config.json`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL with [pgvector](https://github.com/pgvector/pgvector) extension
- An existing MyPalClara `clara.yaml` configuration file

Optional:
- [FalkorDB](https://www.falkordb.com/) for graph memory
- [Redis](https://redis.io/) for caching

## Getting Started

```bash
# Build
dotnet build

# Run (uses ../mypalclara/clara.yaml by default)
dotnet run --project src/Clara.Cli

# Run with explicit config path
dotnet run --project src/Clara.Cli -- --config /path/to/clara.yaml

# Or set via environment variable
export CLARA_YAML_PATH=/path/to/clara.yaml
dotnet run --project src/Clara.Cli
```

## REPL Commands

| Command | Description |
|---------|-------------|
| `!help` | Show available commands |
| `!status` | Show connection status |
| `!mcp list` | List MCP servers |
| `!mcp tools [server]` | List available tools |
| `!memory search <query>` | Search memories |
| `!memory key` | Show key memories |
| `!memory graph <query>` | Search graph relationships |
| `!high <msg>` | Send with Opus tier |
| `!mid <msg>` | Send with Sonnet tier |
| `!low <msg>` | Send with Haiku tier |
| `exit` / `quit` / `bye` | Exit |

## Configuration

Reads directly from the Python project's `clara.yaml`. Key sections:

- `llm.anthropic.*` — Anthropic API credentials and model selection
- `llm.openai_api_key` — OpenAI API key for embeddings
- `memory.rook.*` — Auxiliary LLM for fact/topic/entity extraction
- `memory.vector_store.*` — pgvector connection
- `memory.graph_store.*` — FalkorDB connection
- `memory.redis_url` — Redis cache
- `mcp.servers_dir` — MCP server configs directory
- `database_url` — PostgreSQL for FSRS tables

## Dependencies

| Package | Purpose |
|---------|---------|
| YamlDotNet | Parse clara.yaml |
| ModelContextProtocol | Official .NET MCP SDK |
| Npgsql + Pgvector | PostgreSQL + pgvector |
| Npgsql.EntityFrameworkCore.PostgreSQL | EF Core for FSRS tables |
| StackExchange.Redis | FalkorDB + Redis cache |
| Spectre.Console | Rich terminal UI |
| Microsoft.Extensions.Hosting | DI, config, logging |

## License

Licensed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0). See [LICENSE](LICENSE) for details.
