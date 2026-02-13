# OpenClaw Conversion Plan: .NET + Ruby on Rails

## Executive Summary

This document outlines the plan to convert [OpenClaw](https://github.com/openclaw/openclaw) (formerly Moltbot/Clawdbot) — a ~40,000 LOC TypeScript AI agent platform — into a hybrid **.NET 10** and **Ruby on Rails 8** architecture. The conversion leverages the existing MyPalClara.NET codebase as the foundation for the agent runtime, while introducing Rails for the web-facing components.

---

## Source Analysis: OpenClaw (TypeScript)

### Current Architecture

OpenClaw is a TypeScript monorepo built on Node.js ≥22 with pnpm workspaces. Its architecture follows a hub-and-spoke control plane model:

```
Messaging Channels (WhatsApp, Telegram, Slack, Discord, Signal, Teams, iMessage...)
                    ↓
            Gateway Control Plane (ws://127.0.0.1:18789)
                    ↓
    ├── Pi Agent Runtime (RPC-based LLM execution)
    ├── CLI Interface
    ├── WebChat UI
    ├── macOS/iOS/Android Nodes
    └── Browser Automation (CDP)
```

### Core Components

| Component | Description | LOC (est.) |
|-----------|-------------|------------|
| **Gateway** | WebSocket control plane — session mgmt, presence, event routing, cron, webhooks | ~8,000 |
| **Pi Agent Runtime** | LLM orchestration, tool invocation, streaming, model failover | ~10,000 |
| **Channel Extensions** | WhatsApp (Baileys), Telegram (grammY), Slack (Bolt), Discord (discord.js), etc. | ~8,000 |
| **Skills System** | SKILL.md parsing, ClawHub registry client, skill lifecycle | ~3,000 |
| **Memory & State** | Markdown-based persistence, JSONL sessions, identity/soul files | ~2,000 |
| **Browser/Canvas** | CDP automation, A2UI framework, snapshot capture | ~4,000 |
| **CLI** | Interactive terminal, pairing, doctor diagnostics | ~3,000 |
| **UI (WebChat/Dashboard)** | Web interface for chat, channel mgmt, config | ~2,000 |

### Technology Stack (Current)

- **Runtime**: Node.js ≥22, TypeScript
- **Build**: pnpm, tsx, tsdown, Vitest
- **Gateway Protocol**: WebSocket on port 18789
- **Messaging**: Baileys (WhatsApp), grammY (Telegram), Bolt (Slack), discord.js
- **Storage**: SQLite (local), Markdown files, JSONL session logs
- **Sandboxing**: Docker containers for isolated sub-agent execution
- **Remote Access**: Tailscale Serve/Funnel, SSH tunnels

---

## Target Architecture: .NET 10 + Ruby on Rails 8

### Design Principles

1. **.NET for the engine** — Agent runtime, LLM orchestration, WebSocket gateway, memory subsystem, channel adapters. Performance-critical, long-running processes.
2. **Rails for the web** — Dashboard, ClawHub skill registry, REST/GraphQL APIs, admin panel, WebChat UI. Rapid iteration, convention-over-configuration.
3. **Shared contracts** — Protobuf or JSON Schema for cross-boundary communication.
4. **Preserve MyPalClara.NET** — Reuse existing LLM providers, memory subsystem, EF Core models, MCP integration, and gateway architecture.

### High-Level Architecture

```
                    ┌─────────────────────────────────────┐
                    │         Rails 8 Web Layer            │
                    │  ┌───────────┐  ┌────────────────┐  │
                    │  │ Dashboard │  │ ClawHub        │  │
                    │  │ (Hotwire) │  │ Skill Registry │  │
                    │  └───────────┘  └────────────────┘  │
                    │  ┌───────────┐  ┌────────────────┐  │
                    │  │ WebChat   │  │ REST/GraphQL   │  │
                    │  │ (Turbo)   │  │ API            │  │
                    │  └───────────┘  └────────────────┘  │
                    └──────────────┬──────────────────────┘
                                   │ HTTP/gRPC
                    ┌──────────────┴──────────────────────┐
                    │       .NET 10 Agent Engine            │
                    │  ┌───────────────────────────────┐   │
                    │  │     Gateway Control Plane      │   │
                    │  │  (WebSocket :18789)            │   │
                    │  └───────────────────────────────┘   │
                    │  ┌──────────┐  ┌─────────────────┐   │
                    │  │ Agent    │  │ Memory           │   │
                    │  │ Runtime  │  │ Subsystem        │   │
                    │  │ (LLM     │  │ (pgvector, FSRS, │   │
                    │  │  Orch.)  │  │  FalkorDB, Redis)│   │
                    │  └──────────┘  └─────────────────┘   │
                    │  ┌──────────┐  ┌─────────────────┐   │
                    │  │ Channel  │  │ Skill            │   │
                    │  │ Adapters │  │ Engine           │   │
                    │  └──────────┘  └─────────────────┘   │
                    └──────────────────────────────────────┘
                                   │
              ┌────────────────────┼────────────────────┐
              ↓                    ↓                    ↓
        ┌──────────┐      ┌──────────────┐     ┌─────────────┐
        │PostgreSQL│      │  FalkorDB    │     │   Redis     │
        │ + pgvec  │      │  (Graph)     │     │   (Cache)   │
        └──────────┘      └──────────────┘     └─────────────┘
```

---

## Phase Breakdown

### Phase 1: Foundation & Shared Infrastructure

**Goal**: Establish the project structure, shared contracts, and communication layer between .NET and Rails.

#### 1.1 — Monorepo Structure

```
openclaw/
├── engine/                          # .NET 10 solution
│   ├── OpenClaw.slnx
│   ├── src/
│   │   ├── OpenClaw.Core/           # Shared models, interfaces, config
│   │   ├── OpenClaw.Gateway/        # WebSocket control plane
│   │   ├── OpenClaw.Agent/          # LLM orchestrator + tool runtime
│   │   ├── OpenClaw.Memory/         # Memory subsystem (from MyPalClara)
│   │   ├── OpenClaw.Channels/       # Channel adapter abstractions
│   │   ├── OpenClaw.Channels.Discord/
│   │   ├── OpenClaw.Channels.Telegram/
│   │   ├── OpenClaw.Channels.Slack/
│   │   ├── OpenClaw.Channels.WhatsApp/
│   │   ├── OpenClaw.Channels.Signal/
│   │   ├── OpenClaw.Skills/         # Skill engine + SKILL.md parser
│   │   ├── OpenClaw.Browser/        # CDP browser automation
│   │   ├── OpenClaw.Cli/            # Terminal interface
│   │   └── OpenClaw.Tools/          # Utilities (backfill, diagnostics)
│   └── tests/
│       ├── OpenClaw.Core.Tests/
│       ├── OpenClaw.Gateway.Tests/
│       ├── OpenClaw.Agent.Tests/
│       └── OpenClaw.Memory.Tests/
│
├── web/                             # Rails 8 application
│   ├── app/
│   │   ├── controllers/
│   │   │   ├── dashboard_controller.rb
│   │   │   ├── channels_controller.rb
│   │   │   ├── skills_controller.rb
│   │   │   ├── sessions_controller.rb
│   │   │   ├── settings_controller.rb
│   │   │   └── api/
│   │   │       ├── v1/
│   │   │       │   ├── skills_controller.rb
│   │   │       │   ├── channels_controller.rb
│   │   │       │   ├── sessions_controller.rb
│   │   │       │   └── webhooks_controller.rb
│   │   │       └── graphql_controller.rb
│   │   ├── models/
│   │   │   ├── skill.rb
│   │   │   ├── channel.rb
│   │   │   ├── user.rb
│   │   │   └── pairing.rb
│   │   ├── views/
│   │   │   ├── dashboard/
│   │   │   ├── channels/
│   │   │   ├── skills/
│   │   │   └── webchat/
│   │   ├── javascript/              # Stimulus + Turbo
│   │   └── jobs/
│   │       ├── skill_sync_job.rb
│   │       └── heartbeat_job.rb
│   ├── config/
│   ├── db/
│   │   └── migrate/
│   └── spec/
│
├── proto/                           # Shared Protobuf contracts
│   ├── gateway.proto
│   ├── agent.proto
│   ├── channel.proto
│   ├── skill.proto
│   └── memory.proto
│
├── docker/
│   ├── docker-compose.yml
│   ├── Dockerfile.engine
│   ├── Dockerfile.web
│   └── Dockerfile.sandbox
│
├── skills/                          # Bundled skill definitions
│   └── ...
│
└── docs/
```

#### 1.2 — Cross-Process Communication

| Boundary | Protocol | Why |
|----------|----------|-----|
| Rails → .NET Gateway | gRPC over Unix socket | Low latency, typed contracts, bidirectional streaming |
| .NET Gateway → Rails (events) | Redis Pub/Sub | Decoupled event notification (session state, channel events) |
| External clients → Gateway | WebSocket (OpenClaw v3) | Backward compat with existing CLI/mobile clients |
| External clients → Rails | HTTP/REST + ActionCable | Dashboard, WebChat, API consumers |

#### 1.3 — Database Strategy

| Store | Owner | Purpose |
|-------|-------|---------|
| PostgreSQL + pgvector | .NET (EF Core) | Users, conversations, messages, memory vectors, LLM call logs |
| PostgreSQL | Rails (Active Record) | Skills registry, channel configs, pairings, admin settings |
| FalkorDB | .NET | Entity/relationship graph for memory |
| Redis | Shared | Cache (embeddings, search), Pub/Sub (events), Sidekiq (Rails jobs) |
| SQLite | .NET (optional) | Single-node lightweight mode (compat with original OpenClaw) |

Both .NET and Rails connect to the same PostgreSQL instance but own separate schemas:
- `openclaw_engine` — managed by EF Core migrations
- `openclaw_web` — managed by Rails migrations

---

### Phase 2: .NET Agent Engine (Port from TypeScript)

**Goal**: Convert the OpenClaw TypeScript agent runtime to .NET, building on MyPalClara.NET's existing infrastructure.

#### 2.1 — OpenClaw.Core (from MyPalClara.Core)

Reuse and extend:
- `ILlmProvider` abstraction (Anthropic, OpenAI-compatible) — **exists**
- Configuration system (`appsettings.json` + YAML personality) — **exists**
- Identity/user management — **exists**
- Chat history service — **exists**

Add:
- OpenClaw v3 protocol message types (C# records/DTOs)
- Skill manifest model (`SkillDefinition`, `SkillParameter`, `SkillStep`)
- Channel abstraction (`IChannelAdapter` with `SendAsync`, `OnMessageReceived`)
- Heartbeat/cron scheduling models
- Pairing models and allowlist logic

#### 2.2 — OpenClaw.Gateway (from MyPalClara.Gateway)

Reuse and extend:
- ASP.NET Core hosted service — **exists**
- WebSocket handler infrastructure — **exists**
- Session management — **exists**
- Module loader (plugin architecture) — **exists**
- MCP integration — **exists**

Add:
- OpenClaw v3 protocol implementation (message framing, auth, presence)
- Multi-session support (main session + per-channel group sessions)
- Session activation modes (mention-based, always-on)
- Queue modes for concurrent message handling
- Cron scheduler (Quartz.NET or Hangfire)
- Webhook receiver endpoints
- Event bus (Redis Pub/Sub → `IEventBus` interface)
- gRPC service for Rails communication (`GatewayService.proto`)
- Heartbeat mechanism (`HEARTBEAT.md` parser + scheduled trigger)

#### 2.3 — OpenClaw.Agent (from MyPalClara.Gateway.Orchestration)

Reuse:
- `LlmOrchestrator` multi-turn tool calling — **exists**
- LLM call logging — **exists**
- Streaming response handling — **exists**

Add:
- Tool registry (dynamic tool discovery from skills + MCP + built-ins)
- Sandboxed execution (Docker container spawning for sub-agents)
- Model failover with profile rotation
- Context window management (session pruning strategies)
- System prompt assembly (identity.md + soul.md + memory injection)
- Lane-based concurrency (parallel tool execution with dependency tracking)

#### 2.4 — OpenClaw.Memory (from MyPalClara.Memory)

Reuse entirely — this is the strongest existing asset:
- Vector store with pgvector — **exists**
- FSRS-6 spaced repetition — **exists**
- FalkorDB graph store — **exists**
- Redis embedding cache — **exists**
- Emotional context tracking — **exists**
- Topic recurrence detection — **exists**
- Contradiction detection (5-layer) — **exists**
- Smart ingest pipeline — **exists**

Add:
- Markdown-based memory export (compat with OpenClaw `memory/YYYY-MM-DD.md`)
- `MEMORY.md` long-term fact file sync
- `identity.md` and `soul.md` file watching + hot reload

#### 2.5 — OpenClaw.Channels.*

New implementations for each platform:

| Channel | .NET Library | Priority |
|---------|-------------|----------|
| Discord | Discord.Net 3.x (**exists** in MyPalClara) | P0 |
| Telegram | Telegram.Bot NuGet | P0 |
| Slack | SlackNet NuGet | P1 |
| WhatsApp | WhatsApp Business API (HTTP) | P1 |
| Signal | signal-cli wrapper via Process | P2 |
| iMessage | BlueBubbles HTTP API | P2 |
| Teams | Microsoft Graph SDK | P2 |
| Matrix | Matrix.NET SDK | P3 |
| WebChat | ActionCable via Rails (not .NET) | P0 |

Each adapter implements:
```csharp
public interface IChannelAdapter
{
    string ChannelType { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task SendMessageAsync(ChannelMessage message);
    event Func<InboundMessage, Task> OnMessageReceived;
    Task<PairingResult> HandlePairingAsync(string code);
}
```

#### 2.6 — OpenClaw.Skills

New skill engine:
- `SKILL.md` parser (Markdig-based Markdown → `SkillDefinition`)
- Skill executor (step-by-step with tool invocation)
- ClawHub client (REST API to Rails-hosted registry)
- Bundled skills loader (embedded resources or filesystem)
- Workspace skills discovery (`~/.openclaw/workspace/skills/`)

#### 2.7 — OpenClaw.Browser

New browser automation module:
- PuppeteerSharp or Playwright .NET for CDP
- Page snapshot capture
- Action execution (click, type, navigate)
- Profile management

#### 2.8 — OpenClaw.Cli (from MyPalClara.Cli)

Reuse and extend:
- Spectre.Console REPL — **exists**
- WebSocket client connection — **exists**

Add:
- `openclaw pairing approve` command
- `openclaw doctor` diagnostics
- `clawhub install/search/publish` commands
- `openclaw onboard` guided setup wizard
- Tailscale integration for remote access setup

---

### Phase 3: Ruby on Rails Web Layer

**Goal**: Build the web-facing components that OpenClaw currently handles with its built-in UI.

#### 3.1 — Rails Application Setup

```
Rails 8.0, Ruby 3.3+
- Hotwire (Turbo + Stimulus) for SPA-like interactivity
- Solid Queue for background jobs (Rails 8 default)
- Solid Cache for HTTP caching
- Propshaft for asset pipeline
- Tailwind CSS for styling
- PostgreSQL via Active Record
- Redis for ActionCable + caching
```

#### 3.2 — Dashboard (Hotwire)

The main admin interface:

- **Overview**: Active sessions, connected channels, recent activity feed
- **Channels**: Configure/pair messaging platforms, view connection status, manage allowlists
- **Sessions**: Browse active/archived sessions, view conversation logs
- **Skills**: Browse installed skills, search ClawHub, install/uninstall/configure
- **Memory**: Search memory store, view knowledge graph visualization, manage facts
- **Settings**: LLM provider config, identity/soul editing, automation (cron/webhooks)
- **Logs**: LLM call logs with token usage, latency, cost tracking

Implementation:
```ruby
# Real-time updates via Turbo Streams from Redis events
class DashboardChannel < ApplicationCable::Channel
  def subscribed
    stream_from "dashboard:#{current_user.id}"
  end
end

# Gateway events published to Redis, consumed by Rails
class GatewayEventListener
  def on_session_update(event)
    Turbo::StreamsChannel.broadcast_replace_to(
      "dashboard:#{event.user_id}",
      target: "session-#{event.session_id}",
      partial: "sessions/session",
      locals: { session: event.session }
    )
  end
end
```

#### 3.3 — ClawHub Skill Registry

Full skill marketplace:

```ruby
# Models
class Skill < ApplicationRecord
  has_many :versions, class_name: "SkillVersion"
  has_many :installations
  belongs_to :author, class_name: "User"

  scope :published, -> { where(status: :published) }
  scope :search, ->(q) { where("name ILIKE ? OR description ILIKE ?", "%#{q}%", "%#{q}%") }
end

class SkillVersion < ApplicationRecord
  belongs_to :skill
  has_one_attached :manifest  # SKILL.md file
  validates :semver, format: { with: /\A\d+\.\d+\.\d+\z/ }
end
```

API endpoints:
```
GET    /api/v1/skills              # Search/browse
GET    /api/v1/skills/:slug        # Skill detail
POST   /api/v1/skills              # Publish new skill
GET    /api/v1/skills/:slug/download  # Download skill manifest
POST   /api/v1/skills/:slug/install   # Register installation
DELETE /api/v1/skills/:slug/install   # Uninstall
```

#### 3.4 — WebChat (Turbo + ActionCable)

Browser-based chat interface connecting to the .NET gateway:

```ruby
# WebChat controller serves the chat UI
class WebchatController < ApplicationController
  def show
    @session = current_user.sessions.find_or_create_by(channel: "webchat")
  end
end

# ActionCable channel bridges browser ↔ .NET Gateway
class WebchatChannel < ApplicationCable::Channel
  def subscribed
    stream_from "webchat:#{current_user.id}"
    @gateway_client = GatewayClient.connect(current_user)
  end

  def receive(data)
    @gateway_client.send_message(data["message"])
  end

  def unsubscribed
    @gateway_client&.disconnect
  end
end
```

#### 3.5 — REST & GraphQL API

For mobile apps, CLI, and third-party integrations:

```ruby
# REST API
module Api::V1
  class SessionsController < ApiController
    def index
      render json: current_user.sessions.active
    end

    def messages
      session = current_user.sessions.find(params[:id])
      render json: session.messages.recent(limit: params[:limit] || 50)
    end
  end
end

# GraphQL (optional, via graphql-ruby gem)
class Types::QueryType < Types::BaseObject
  field :sessions, [Types::SessionType], null: false
  field :skills, [Types::SkillType], null: false
  field :memory_search, [Types::MemoryType], null: false do
    argument :query, String, required: true
  end
end
```

#### 3.6 — Background Jobs (Solid Queue)

```ruby
class SkillSyncJob < ApplicationJob
  queue_as :default

  def perform(user_id)
    user = User.find(user_id)
    ClawHubClient.sync_installed_skills(user)
  end
end

class HeartbeatDispatchJob < ApplicationJob
  queue_as :low

  def perform(user_id)
    GatewayClient.trigger_heartbeat(user_id)
  end
end

class ChannelHealthCheckJob < ApplicationJob
  queue_as :default

  def perform
    Channel.active.find_each do |channel|
      GatewayClient.ping_channel(channel.id)
    end
  end
end
```

---

### Phase 4: Integration & Cross-Cutting Concerns

#### 4.1 — Authentication & Authorization

| Layer | Mechanism |
|-------|-----------|
| WebSocket (Gateway) | Token-based auth (JWT or HMAC signed tokens) |
| Rails Web | Devise with session cookies |
| Rails API | Token auth (doorkeeper or jwt) |
| Channel Pairing | Code-based pairing with approval workflow |
| Tailscale | Identity headers (funnel mode: password) |

Shared user identity across .NET and Rails via the same PostgreSQL `users` table.

#### 4.2 — Observability

| Concern | .NET | Rails |
|---------|------|-------|
| Logging | Serilog → structured JSON | Rails logger + Lograge |
| Metrics | OpenTelemetry .NET SDK | OpenTelemetry Ruby SDK |
| Tracing | Distributed trace context propagation across gRPC boundary |
| Health | ASP.NET health checks | Rails `/up` endpoint |

#### 4.3 — Security Hardening

Addressing OpenClaw's known security weaknesses:
- **No plaintext secrets**: Use ASP.NET Data Protection + Rails encrypted credentials
- **Sandboxed execution**: Docker containers for untrusted skill execution (existing pattern)
- **Input validation**: Parameterized queries (EF Core + Active Record) everywhere
- **CVE-2026-25253 mitigation**: Auth token rotation, secure token storage, HMAC validation
- **Skill vetting**: Automated static analysis of SKILL.md before registry publication

#### 4.4 — Configuration

Unified config approach:
```
~/.openclaw/
├── openclaw.json          # Main config (read by both .NET and Rails)
├── identity.md            # Core AI personality
├── soul.md                # Behavioral boundaries
├── heartbeat.md           # Scheduled task definitions
├── workspace/
│   ├── AGENTS.md
│   ├── TOOLS.md
│   └── skills/
│       └── <skill-name>/
│           └── SKILL.md
└── data/
    ├── memory/            # Daily memory markdown files
    └── sessions/          # JSONL session logs
```

---

### Phase 5: Testing Strategy

| Layer | Framework | Coverage Target |
|-------|-----------|----------------|
| .NET Unit | xUnit + Moq | Core, Agent, Memory, Skills |
| .NET Integration | WebApplicationFactory + Testcontainers | Gateway, Channels |
| .NET E2E | Custom test harness | Full message flow |
| Rails Unit | RSpec + FactoryBot | Models, Services |
| Rails Request | RSpec request specs | API endpoints |
| Rails System | Capybara + Playwright | Dashboard, WebChat UI |
| Cross-boundary | Docker Compose test env | gRPC contracts, Redis events |

---

### Phase 6: Deployment

#### Docker Compose (Development / Single-Node)

```yaml
services:
  engine:
    build: ./docker/Dockerfile.engine
    ports: ["18789:18789"]
    depends_on: [postgres, redis, falkordb]

  web:
    build: ./docker/Dockerfile.web
    ports: ["3000:3000"]
    depends_on: [postgres, redis, engine]

  postgres:
    image: pgvector/pgvector:pg17
    ports: ["5432:5432"]

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]

  falkordb:
    image: falkordb/falkordb:latest
    ports: ["6480:6379"]

  sandbox:
    build: ./docker/Dockerfile.sandbox
    # Spawned on-demand by engine for skill execution
```

#### Production

- .NET engine as systemd service or Kubernetes deployment
- Rails via Puma behind Nginx/Caddy
- Managed PostgreSQL (RDS, Cloud SQL, etc.)
- Managed Redis (ElastiCache, Memorystore)
- Optional: Cloudflare Workers for edge WebSocket termination (Moltworker pattern)

---

## Migration Mapping: TypeScript → .NET/Rails

| OpenClaw TS Component | Target | .NET/Rails Equivalent |
|----------------------|--------|----------------------|
| `apps/gateway/` | .NET | `OpenClaw.Gateway` (ASP.NET WebSocket host) |
| `src/agent/` (Pi Runtime) | .NET | `OpenClaw.Agent` (LlmOrchestrator + tool registry) |
| `extensions/telegram/` | .NET | `OpenClaw.Channels.Telegram` |
| `extensions/discord/` | .NET | `OpenClaw.Channels.Discord` (Discord.Net) |
| `extensions/slack/` | .NET | `OpenClaw.Channels.Slack` |
| `extensions/whatsapp/` | .NET | `OpenClaw.Channels.WhatsApp` |
| `extensions/signal/` | .NET | `OpenClaw.Channels.Signal` |
| `extensions/webchat/` | Rails | `WebchatChannel` (ActionCable) |
| `skills/` | .NET + Rails | Engine: `OpenClaw.Skills`, Registry: Rails `Skill` model |
| `ui/` (dashboard) | Rails | Hotwire dashboard controllers/views |
| `src/browser/` | .NET | `OpenClaw.Browser` (Playwright .NET) |
| Memory (Markdown files) | .NET | `OpenClaw.Memory` (pgvector + FSRS + FalkorDB) |
| `src/cli/` | .NET | `OpenClaw.Cli` (Spectre.Console) |
| ClawHub registry API | Rails | `Api::V1::SkillsController` |
| Cron/webhooks | .NET + Rails | Engine: Quartz.NET, Web: Solid Queue |
| Docker sandbox | .NET | `Docker.DotNet` library for container lifecycle |
| Tailscale integration | .NET | CLI wrapper + config generation |

---

## Reuse from MyPalClara.NET

The existing MyPalClara.NET codebase provides significant head start:

| MyPalClara Component | OpenClaw Equivalent | Reuse Level |
|---------------------|---------------------|-------------|
| `MyPalClara.Core` | `OpenClaw.Core` | ~70% (extend with OC protocol types) |
| `MyPalClara.Gateway` | `OpenClaw.Gateway` | ~60% (add OC v3 protocol, multi-session) |
| `LlmOrchestrator` | `OpenClaw.Agent` | ~80% (add tool registry, sandboxing) |
| `MyPalClara.Memory` | `OpenClaw.Memory` | ~90% (add MD export/import) |
| `MyPalClara.Discord` | `OpenClaw.Channels.Discord` | ~85% (add group sessions, pairing) |
| `MyPalClara.Cli` | `OpenClaw.Cli` | ~50% (add OC-specific commands) |
| `MyPalClara.Voice` | `OpenClaw.Voice` (future) | ~95% (direct reuse) |
| `MyPalClara.Ssh` | `OpenClaw.Ssh` (future) | ~95% (direct reuse) |
| EF Core data model | Shared DB schema | ~60% (extend with OC entities) |
| Docker Compose | Docker setup | ~80% (add web service) |

---

## Implementation Priority

### Milestone 1: Core Engine (Weeks 1-4)
- [ ] Project scaffolding (monorepo, solution, Rails app)
- [ ] Protobuf contract definitions
- [ ] `OpenClaw.Core` (extend MyPalClara.Core with OC types)
- [ ] `OpenClaw.Gateway` (OC v3 protocol on existing gateway)
- [ ] `OpenClaw.Agent` (extend LlmOrchestrator)
- [ ] `OpenClaw.Memory` (rebrand + MD export)
- [ ] gRPC bridge between .NET and Rails

### Milestone 2: Channels & Skills (Weeks 5-8)
- [ ] `OpenClaw.Channels.Discord` (extend existing)
- [ ] `OpenClaw.Channels.Telegram` (new)
- [ ] `OpenClaw.Channels.Slack` (new)
- [ ] `OpenClaw.Skills` engine + SKILL.md parser
- [ ] Pairing workflow (code-based approval)
- [ ] Heartbeat/cron scheduler

### Milestone 3: Web Layer (Weeks 5-8, parallel with M2)
- [ ] Rails app scaffold + Hotwire setup
- [ ] Dashboard (sessions, channels, overview)
- [ ] ClawHub skill registry (CRUD + search)
- [ ] WebChat (ActionCable ↔ Gateway bridge)
- [ ] REST API v1
- [ ] Settings/config management UI

### Milestone 4: Advanced Features (Weeks 9-12)
- [ ] `OpenClaw.Browser` (Playwright .NET CDP)
- [ ] Docker sandbox for sub-agent execution
- [ ] WhatsApp + Signal channel adapters
- [ ] `openclaw doctor` diagnostics
- [ ] Tailscale remote access integration
- [ ] Skill vetting pipeline

### Milestone 5: Production Readiness (Weeks 13-16)
- [ ] Security audit (auth, sandboxing, input validation)
- [ ] OpenTelemetry instrumentation
- [ ] Comprehensive test suite
- [ ] Documentation (setup, API, contributing)
- [ ] Docker production images
- [ ] CI/CD pipeline

---

## Open Questions

1. **GraphQL vs REST-only**: Should the Rails API include GraphQL, or is REST sufficient for v1?
2. **Skill execution boundary**: Should skill code run in .NET (Docker sandbox) or Rails (Solid Queue)?
3. **Multi-tenant support**: Is this single-user (like original OpenClaw) or multi-user from day one?
4. **Mobile apps**: Will iOS/Android apps connect directly to .NET Gateway or through Rails API?
5. **WhatsApp approach**: Business API (official, paid) vs Baileys-style (unofficial, free)?
6. **Backward compatibility**: Must the .NET Gateway speak exact OpenClaw v3 protocol for existing CLI/mobile clients?

---

## References

- [OpenClaw Main Repository](https://github.com/openclaw/openclaw)
- [OpenClaw Gateway (Python)](https://github.com/loserbcc/openclaw-gateway)
- [OpenClaw Coolify Deployment](https://github.com/essamamdani/openclaw-coolify)
- [OpenClaw Skills Collection](https://github.com/VoltAgent/awesome-openclaw-skills)
- [Moltworker (Cloudflare)](https://github.com/cloudflare/moltworker)
- [OpenClaw Architecture Analysis](https://sterlites.com/blog/moltbot-local-first-ai-agents-guide-2026)
- [OpenClaw Wikipedia](https://en.wikipedia.org/wiki/OpenClaw)
- [MyPalClara.NET (this repo)](https://github.com/heidornj/mypalclara) — Foundation codebase
