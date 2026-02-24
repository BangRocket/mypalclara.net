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

## License

Private -- MyPalClara project.
