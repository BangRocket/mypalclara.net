# CLAUDE.md

Guidance for Claude Code working with this repository.

## Project Overview

Clara is a .NET 10 AI agent gateway. It provides an ASP.NET Core host with HTTP API and WebSocket support, LLM orchestration with tool calling, and persistent memory.

This is a ground-up rewrite (Phase 0 complete). The previous MyPalClara.* project structure has been replaced.

## Quick Reference

```bash
# Build
dotnet build

# Run (development)
dotnet run --project src/Clara.Gateway

# Run tests
dotnet test

# Docker
docker compose -f docker/docker-compose.yml up
```

## Architecture

```
Clara.Core      -> Domain: entities, DbContext, config, LLM/memory/tool interfaces
Clara.Gateway   -> ASP.NET Core host: HTTP API, WebSocket, pipeline, services
```

### Project Structure

```
src/
  Clara.Core/
    Config/          # Strongly-typed options (ClaraOptions, LlmOptions, etc.)
    Data/
      Entities/      # EF Core entities (8 files)
      ClaraDbContext  # Fluent API mapping, provider-aware (PostgreSQL/SQLite)
    Events/          # Event bus (future)
    Llm/
      Providers/     # LLM provider implementations (future)
      ToolCalling/   # Tool call handling (future)
    Memory/          # Memory system (future)
    Prompt/          # Prompt building (future)
    Sessions/        # Session management (future)
    SubAgents/       # Sub-agent orchestration (future)
    Tools/
      BuiltIn/       # Built-in tools (future)
      Mcp/           # MCP tool integration (future)
      ToolPolicy/    # Tool policy enforcement (future)

  Clara.Gateway/
    Api/             # HTTP API controllers (future)
    Hooks/           # Shell/C# hooks (future)
    Hubs/            # SignalR hubs (future)
    Pipeline/
      Stages/        # Processing pipeline stages (future)
      Middleware/    # Pipeline middleware (future)
    Queues/          # Message queues (future)
    Sandbox/         # Docker sandbox (future)
    Services/        # Background services (future)

tests/
  Clara.Core.Tests/
  Clara.Gateway.Tests/

docker/
  Dockerfile         # Multi-stage build
  docker-compose.yml # Gateway + PostgreSQL (pgvector) + Redis

workspace/
  persona.md         # Clara's persona definition
  tools.md           # Tool usage conventions
  heartbeat.md       # Periodic checklist
```

### Key Design Decisions

- **Provider-aware DbContext** -- `OnModelCreating` detects PostgreSQL vs SQLite; jsonb and vector column types only applied for PostgreSQL; Embedding ignored for SQLite
- **No EF Core migrations** -- schema mapping only via Fluent API
- **Strongly-typed config** -- `ClaraOptions` root with nested options for each subsystem, bound to "Clara" config section

### Entities

8 entities in `Clara.Core.Data.Entities/`:
- `UserEntity` -- platform users
- `SessionEntity` -- conversation sessions (has many Messages)
- `MessageEntity` -- individual messages in a session
- `MemoryEntity` -- vector memories with pgvector Embedding
- `ProjectEntity` -- project metadata
- `McpServerEntity` -- MCP server configurations
- `EmailAccountEntity` -- email polling accounts
- `ToolUsageEntity` -- tool call audit log

### Configuration

Root: `ClaraOptions` (section: "Clara")
- `LlmOptions` -- provider selection, API keys, model tiers
- `MemoryOptions` -- embedding model, search limits, extraction
- `GatewayOptions` -- host, port, secret, tool limits
- `ToolOptions` -- loop detection, policies per channel
- `SandboxOptions` -- Docker container settings
- `HeartbeatOptions` -- periodic checklist
- `SubAgentOptions` -- sub-agent limits
- `DiscordOptions` -- bot token, servers, stop phrases

## Testing

```bash
dotnet test                                  # All tests
dotnet test tests/Clara.Core.Tests          # Core tests only
dotnet test tests/Clara.Gateway.Tests       # Gateway tests only
```

Tests use SQLite in-memory databases. The DbContext is provider-aware so Embedding columns are ignored under SQLite.

## NuGet Packages

### Clara.Core
- Microsoft.EntityFrameworkCore (10.x)
- Npgsql.EntityFrameworkCore.PostgreSQL (10.x preview)
- Microsoft.EntityFrameworkCore.Sqlite (10.x)
- Pgvector.EntityFrameworkCore (0.3.0)
- Microsoft.Extensions.Http, DI, Logging, Options (10.x)
- Anthropic (0.x)
- OpenAI (2.x)
- YamlDotNet (16.x)

### Clara.Gateway
- Serilog.AspNetCore (9.x)
- Docker.DotNet (3.x)
