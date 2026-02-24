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
