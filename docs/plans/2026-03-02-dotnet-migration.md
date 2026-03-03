# MyPalClara .NET Migration — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ground-up rewrite of MyPalClara in .NET 10, replacing all existing code with the new Clara.Core / Clara.Gateway / Clara.Adapters.* architecture described in `docs/MyPalClara_DotNet_Migration_Plan.md`.

**Architecture:** Two-project core: `Clara.Core` (library with LLM, memory, tools, sessions, prompts, events, data) and `Clara.Gateway` (ASP.NET Core host with pipeline, queues, hubs, services, sandbox, hooks, scheduler, API). Adapters (`Clara.Adapters.Discord`, `.Teams`, `.Cli`) connect via SignalR. PostgreSQL + pgvector replaces Qdrant. Lane queues serialize per-session. Composable prompt files (`persona.md`, `tools.md`, `user.md`). Tool selector injects only relevant tools. Agentic loop with tool loop detection.

**Tech Stack:** .NET 10, ASP.NET Core, SignalR, EF Core + Npgsql + pgvector, Anthropic SDK, OpenAI SDK (for OpenRouter/compatible), DSharpPlus, xUnit, NSubstitute, Serilog, Docker

**Reference:** `docs/MyPalClara_DotNet_Migration_Plan.md` (the full design document — all interfaces, data flow, config structure are defined there)

---

## Phase 0: Foundation

### Task 0.1: Clean slate — delete existing code, create solution

**Files:**
- Delete: all of `src/`, `tests/`, any `*.sln` or `*.slnx` files
- Create: `Clara.sln`
- Create: `src/Clara.Core/Clara.Core.csproj`
- Create: `src/Clara.Gateway/Clara.Gateway.csproj`
- Create: `tests/Clara.Core.Tests/Clara.Core.Tests.csproj`
- Create: `tests/Clara.Gateway.Tests/Clara.Gateway.Tests.csproj`

**Steps:**

1. Delete existing source:
```bash
rm -rf src/ tests/
rm -f *.sln *.slnx
```

2. Create solution and projects:
```bash
dotnet new sln -n Clara
dotnet new classlib -n Clara.Core -o src/Clara.Core --framework net10.0
dotnet new web -n Clara.Gateway -o src/Clara.Gateway --framework net10.0
dotnet new xunit -n Clara.Core.Tests -o tests/Clara.Core.Tests --framework net10.0
dotnet new xunit -n Clara.Gateway.Tests -o tests/Clara.Gateway.Tests --framework net10.0
```

3. Add projects to solution:
```bash
dotnet sln add src/Clara.Core/Clara.Core.csproj
dotnet sln add src/Clara.Gateway/Clara.Gateway.csproj
dotnet sln add tests/Clara.Core.Tests/Clara.Core.Tests.csproj
dotnet sln add tests/Clara.Gateway.Tests/Clara.Gateway.Tests.csproj
```

4. Add project references:
```bash
dotnet add src/Clara.Gateway reference src/Clara.Core
dotnet add tests/Clara.Core.Tests reference src/Clara.Core
dotnet add tests/Clara.Gateway.Tests reference src/Clara.Gateway
```

5. Verify build:
```bash
dotnet build
dotnet test
```

Expected: BUILD SUCCEEDED, tests pass (default template tests).

6. Commit:
```bash
git add Clara.sln src/ tests/
git commit -m "chore: clean slate — new Clara.Core + Clara.Gateway solution structure"
```

---

### Task 0.2: Clara.Core project setup — NuGet packages and directory structure

**Files:**
- Modify: `src/Clara.Core/Clara.Core.csproj`
- Create: directory structure with placeholder files

**Steps:**

1. Set up Clara.Core.csproj with required NuGet packages:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Clara.Core</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- EF Core + PostgreSQL + pgvector -->
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0-*" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0-*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0-*" />
    <PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.4.1" />

    <!-- HTTP + DI -->
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0-*" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0-*" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0-*" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0-*" />

    <!-- LLM SDKs -->
    <PackageReference Include="Anthropic" Version="0.*" />
    <PackageReference Include="OpenAI" Version="2.*" />

    <!-- Utilities -->
    <PackageReference Include="YamlDotNet" Version="16.*" />
  </ItemGroup>
</Project>
```

2. Create directory structure:
```bash
mkdir -p src/Clara.Core/{Llm/Providers,Llm/ToolCalling,Memory,Prompt,Tools/{ToolPolicy,BuiltIn,Mcp},Sessions,SubAgents,Config,Events,Data/Entities}
```

3. Remove template Class1.cs:
```bash
rm src/Clara.Core/Class1.cs
```

4. Verify build:
```bash
dotnet build src/Clara.Core
```

5. Commit.

---

### Task 0.3: Clara.Gateway project setup — NuGet packages and directory structure

**Files:**
- Modify: `src/Clara.Gateway/Clara.Gateway.csproj`
- Create: directory structure

**Steps:**

1. Set up Clara.Gateway.csproj:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Clara.Gateway</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Clara.Core\Clara.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Serilog.AspNetCore" Version="10.*" />
    <PackageReference Include="Docker.DotNet" Version="3.*" />
  </ItemGroup>
</Project>
```

2. Create directory structure:
```bash
mkdir -p src/Clara.Gateway/{Pipeline/Stages,Pipeline/Middleware,Queues,Hubs,Services,Hooks,Sandbox,Api}
```

3. Verify build:
```bash
dotnet build
```

4. Commit.

---

### Task 0.4: EF Core entities and ClaraDbContext

**Files:**
- Create: `src/Clara.Core/Data/ClaraDbContext.cs`
- Create: `src/Clara.Core/Data/Entities/UserEntity.cs`
- Create: `src/Clara.Core/Data/Entities/SessionEntity.cs`
- Create: `src/Clara.Core/Data/Entities/MessageEntity.cs`
- Create: `src/Clara.Core/Data/Entities/MemoryEntity.cs`
- Create: `src/Clara.Core/Data/Entities/ProjectEntity.cs`
- Create: `src/Clara.Core/Data/Entities/McpServerEntity.cs`
- Create: `src/Clara.Core/Data/Entities/EmailAccountEntity.cs`
- Create: `src/Clara.Core/Data/Entities/ToolUsageEntity.cs`
- Test: `tests/Clara.Core.Tests/Data/ClaraDbContextTests.cs`

**Steps:**

1. Write entity classes. Key entities with their columns (reference migration plan §6.1):

**UserEntity:**
```csharp
namespace Clara.Core.Data.Entities;

public class UserEntity
{
    public Guid Id { get; set; }
    public string PlatformId { get; set; } = "";
    public string Platform { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string? Preferences { get; set; } // JSONB
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**SessionEntity:**
```csharp
namespace Clara.Core.Data.Entities;

public class SessionEntity
{
    public Guid Id { get; set; }
    public string SessionKey { get; set; } = ""; // structured key: clara:main:discord:dm:123
    public Guid? UserId { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = "active"; // active, archived, timeout
    public string? Summary { get; set; }
    public string? Metadata { get; set; } // JSONB
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public UserEntity? User { get; set; }
    public List<MessageEntity> Messages { get; set; } = [];
}
```

**MessageEntity:**
```csharp
namespace Clara.Core.Data.Entities;

public class MessageEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = ""; // user, assistant, system, tool
    public string Content { get; set; } = "";
    public string? ToolCalls { get; set; } // JSONB
    public string? ToolResults { get; set; } // JSONB
    public string? Metadata { get; set; } // JSONB
    public int TokenCount { get; set; }
    public DateTime CreatedAt { get; set; }

    public SessionEntity? Session { get; set; }
}
```

**MemoryEntity (with pgvector):**
```csharp
using Pgvector;

namespace Clara.Core.Data.Entities;

public class MemoryEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Content { get; set; } = "";
    public Vector? Embedding { get; set; } // pgvector
    public string? Category { get; set; }
    public float Score { get; set; } = 1.0f;
    public string? Metadata { get; set; } // JSONB
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
}
```

**ProjectEntity:**
```csharp
namespace Clara.Core.Data.Entities;

public class ProjectEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Path { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**McpServerEntity:**
```csharp
namespace Clara.Core.Data.Entities;

public class McpServerEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Transport { get; set; } = "stdio"; // stdio, http
    public string Command { get; set; } = "";
    public string? Args { get; set; }
    public string? Env { get; set; } // JSONB
    public string? OAuthConfig { get; set; } // JSONB
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**EmailAccountEntity:**
```csharp
namespace Clara.Core.Data.Entities;

public class EmailAccountEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string ImapHost { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public string? Username { get; set; }
    public string? EncryptedPassword { get; set; }
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 300;
    public DateTime? LastPolledAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**ToolUsageEntity:**
```csharp
namespace Clara.Core.Data.Entities;

public class ToolUsageEntity
{
    public Guid Id { get; set; }
    public string ToolName { get; set; } = "";
    public string SessionKey { get; set; } = "";
    public string? UserId { get; set; }
    public string? Arguments { get; set; } // JSONB
    public bool Success { get; set; }
    public int DurationMs { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

2. Write ClaraDbContext with Fluent API:

```csharp
using Clara.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clara.Core.Data;

public class ClaraDbContext : DbContext
{
    public ClaraDbContext(DbContextOptions<ClaraDbContext> options) : base(options) { }

    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<MemoryEntity> Memories => Set<MemoryEntity>();
    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();
    public DbSet<McpServerEntity> McpServers => Set<McpServerEntity>();
    public DbSet<EmailAccountEntity> EmailAccounts => Set<EmailAccountEntity>();
    public DbSet<ToolUsageEntity> ToolUsages => Set<ToolUsageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<UserEntity>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Preferences).HasColumnType("jsonb");
        });

        modelBuilder.Entity<SessionEntity>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SessionKey);
            e.Property(x => x.Metadata).HasColumnType("jsonb");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasMany(x => x.Messages).WithOne(x => x.Session).HasForeignKey(x => x.SessionId);
        });

        modelBuilder.Entity<MessageEntity>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SessionId);
            e.Property(x => x.ToolCalls).HasColumnType("jsonb");
            e.Property(x => x.ToolResults).HasColumnType("jsonb");
            e.Property(x => x.Metadata).HasColumnType("jsonb");
        });

        modelBuilder.Entity<MemoryEntity>(e =>
        {
            e.ToTable("memories");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Embedding).HasColumnType("vector(1536)");
            e.Property(x => x.Metadata).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ProjectEntity>(e =>
        {
            e.ToTable("projects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Metadata).HasColumnType("jsonb");
        });

        modelBuilder.Entity<McpServerEntity>(e =>
        {
            e.ToTable("mcp_servers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Env).HasColumnType("jsonb");
            e.Property(x => x.OAuthConfig).HasColumnType("jsonb");
        });

        modelBuilder.Entity<EmailAccountEntity>(e =>
        {
            e.ToTable("email_accounts");
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<ToolUsageEntity>(e =>
        {
            e.ToTable("tool_usages");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ToolName);
            e.Property(x => x.Arguments).HasColumnType("jsonb");
        });
    }
}
```

3. Write test verifying DbContext can be created with SQLite:

```csharp
using Clara.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Clara.Core.Tests.Data;

public class ClaraDbContextTests
{
    private ClaraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ClaraDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var ctx = new ClaraDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task CanCreateAndQueryUser()
    {
        using var ctx = CreateContext();
        ctx.Users.Add(new() { Id = Guid.NewGuid(), PlatformId = "123", Platform = "discord", DisplayName = "Test" });
        await ctx.SaveChangesAsync();
        Assert.Single(await ctx.Users.ToListAsync());
    }

    [Fact]
    public async Task SessionHasMessages()
    {
        using var ctx = CreateContext();
        var session = new Clara.Core.Data.Entities.SessionEntity
        {
            Id = Guid.NewGuid(),
            SessionKey = "clara:main:discord:dm:123",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        session.Messages.Add(new()
        {
            Id = Guid.NewGuid(),
            Role = "user",
            Content = "Hello",
            CreatedAt = DateTime.UtcNow
        });
        ctx.Sessions.Add(session);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Sessions.Include(s => s.Messages).FirstAsync();
        Assert.Single(loaded.Messages);
    }
}
```

4. Run tests:
```bash
dotnet test tests/Clara.Core.Tests
```

5. Commit.

---

### Task 0.5: Configuration — strongly-typed options

**Files:**
- Create: `src/Clara.Core/Config/ClaraOptions.cs`
- Create: `src/Clara.Core/Config/LlmOptions.cs`
- Create: `src/Clara.Core/Config/MemoryOptions.cs`
- Create: `src/Clara.Core/Config/GatewayOptions.cs`
- Create: `src/Clara.Core/Config/ToolOptions.cs`
- Create: `src/Clara.Core/Config/SandboxOptions.cs`
- Create: `src/Clara.Core/Config/HeartbeatOptions.cs`
- Create: `src/Clara.Core/Config/SubAgentOptions.cs`
- Create: `src/Clara.Core/Config/DiscordOptions.cs`
- Create: `src/Clara.Gateway/appsettings.json`

**Steps:**

1. Write options classes matching §7.1 of the migration plan. Each class maps to a section of the config.

2. Write appsettings.json matching the structure in §7.1.

3. Verify build.

4. Commit.

---

### Task 0.6: Docker Compose — PostgreSQL + pgvector

**Files:**
- Create: `docker/docker-compose.yml`
- Create: `docker/Dockerfile`

**Steps:**

1. Write docker-compose.yml with PostgreSQL (pgvector extension), Redis (optional), and the gateway service.

2. Write multi-stage Dockerfile.

3. Verify:
```bash
docker compose -f docker/docker-compose.yml config
```

4. Commit.

---

### Task 0.7: Workspace files

**Files:**
- Create: `workspace/persona.md`
- Create: `workspace/tools.md`
- Create: `workspace/heartbeat.md`
- Create: `workspace/users/.gitkeep`
- Create: `workspace/skills/.gitkeep`
- Create: `workspace/memory-export/.gitkeep`

**Steps:**

1. Create Clara's persona file (port from Python system prompt — reference the existing persona).

2. Create tool conventions file.

3. Create heartbeat checklist template.

4. Commit.

---

## Phase 1: Core Intelligence

### Task 1.1: LLM abstractions — interfaces and records

**Files:**
- Create: `src/Clara.Core/Llm/ILlmProvider.cs`
- Create: `src/Clara.Core/Llm/ILlmProviderFactory.cs`
- Create: `src/Clara.Core/Llm/LlmRequest.cs`
- Create: `src/Clara.Core/Llm/LlmResponse.cs`
- Create: `src/Clara.Core/Llm/LlmStreamChunk.cs`
- Create: `src/Clara.Core/Llm/LlmMessage.cs`
- Create: `src/Clara.Core/Llm/LlmContent.cs`
- Create: `src/Clara.Core/Llm/LlmRole.cs`
- Create: `src/Clara.Core/Llm/ModelTier.cs`

**Steps:**

1. Implement all types exactly as specified in migration plan §4.1:

```csharp
// ILlmProvider.cs
public interface ILlmProvider
{
    string Name { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
    IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, CancellationToken ct = default);
}

// LlmContent.cs
public abstract record LlmContent;
public record TextContent(string Text) : LlmContent;
public record ImageContent(string Base64, string MediaType) : LlmContent;
public record ToolCallContent(string Id, string Name, JsonElement Arguments) : LlmContent;
public record ToolResultContent(string ToolCallId, string Content, bool IsError = false) : LlmContent;

// LlmMessage.cs
public record LlmMessage(LlmRole Role, IReadOnlyList<LlmContent> Content);

// LlmRequest.cs
public record LlmRequest(
    string Model,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<ToolDefinition>? Tools = null,
    float Temperature = 0.7f,
    int? MaxTokens = null);

// LlmResponse.cs
public record LlmResponse(
    IReadOnlyList<LlmContent> Content,
    string? StopReason,
    LlmUsage Usage);

public record LlmUsage(int InputTokens, int OutputTokens);

// LlmStreamChunk.cs
public record LlmStreamChunk
{
    public string? TextDelta { get; init; }
    public ToolCallContent? ToolCall { get; init; }
    public bool IsTextDelta => TextDelta is not null;
    public bool IsToolCall => ToolCall is not null;
}

// ModelTier.cs
public enum ModelTier { High, Mid, Low }

// LlmRole.cs
public enum LlmRole { System, User, Assistant, Tool }
```

2. Also create `ToolDefinition.cs`:
```csharp
public record ToolDefinition(string Name, string Description, JsonElement ParameterSchema);
```

3. Verify build.

4. Commit.

---

### Task 1.2: AnthropicProvider

**Files:**
- Create: `src/Clara.Core/Llm/Providers/AnthropicProvider.cs`
- Test: `tests/Clara.Core.Tests/Llm/AnthropicProviderTests.cs`

**Steps:**

1. Implement AnthropicProvider using the Anthropic NuGet SDK. Handle:
   - Message conversion (LlmMessage → Anthropic SDK format)
   - Streaming via IAsyncEnumerable
   - Tool call parsing from stream
   - System message extraction (Anthropic uses separate system param)

2. Write unit tests with mocked HTTP (use NSubstitute or manual mock of the Anthropic client). Test:
   - Basic completion
   - Streaming text deltas
   - Tool call extraction from response

3. Run tests.

4. Commit.

---

### Task 1.3: OpenAiCompatProvider (OpenRouter, OpenAI, NanoGPT)

**Files:**
- Create: `src/Clara.Core/Llm/Providers/OpenAiCompatProvider.cs`
- Test: `tests/Clara.Core.Tests/Llm/OpenAiCompatProviderTests.cs`

**Steps:**

1. Implement using the OpenAI NuGet SDK. This single provider handles OpenRouter, native OpenAI, and NanoGPT by varying the base URL.

2. Handle:
   - Message conversion (LlmMessage → OpenAI chat format)
   - Streaming via IAsyncEnumerable
   - Function/tool call parsing
   - Base URL configuration for different providers

3. Write unit tests. Test streaming and tool calls.

4. Commit.

---

### Task 1.4: LlmProviderFactory + TierClassifier

**Files:**
- Create: `src/Clara.Core/Llm/LlmProviderFactory.cs`
- Create: `src/Clara.Core/Llm/TierClassifier.cs`
- Test: `tests/Clara.Core.Tests/Llm/LlmProviderFactoryTests.cs`
- Test: `tests/Clara.Core.Tests/Llm/TierClassifierTests.cs`

**Steps:**

1. LlmProviderFactory resolves ILlmProvider by name from config. Supports tier-based model selection.

2. TierClassifier uses a simple heuristic (message length, presence of code, complexity markers) to auto-select tier. Can optionally use LLM-based classification.

3. Write tests for factory resolution and tier classification.

4. Commit.

---

### Task 1.5: Memory system — IMemoryStore + PgVectorMemoryStore

**Files:**
- Create: `src/Clara.Core/Memory/IMemoryStore.cs`
- Create: `src/Clara.Core/Memory/IMemoryView.cs`
- Create: `src/Clara.Core/Memory/MemoryEntry.cs`
- Create: `src/Clara.Core/Memory/MemorySearchResult.cs`
- Create: `src/Clara.Core/Memory/MemoryMetadata.cs`
- Create: `src/Clara.Core/Memory/PgVectorMemoryStore.cs`
- Create: `src/Clara.Core/Memory/EmbeddingProvider.cs`
- Test: `tests/Clara.Core.Tests/Memory/PgVectorMemoryStoreTests.cs`

**Steps:**

1. Implement interfaces exactly as specified in migration plan §4.2.

2. PgVectorMemoryStore uses ClaraDbContext + pgvector for storage and cosine similarity search.

3. EmbeddingProvider wraps OpenAI text-embedding-3-small API.

4. Write tests using SQLite in-memory (note: pgvector-specific features need integration tests, but CRUD operations can be tested with SQLite).

5. Commit.

---

### Task 1.6: MemoryExtractor + SessionSummarizer

**Files:**
- Create: `src/Clara.Core/Memory/MemoryExtractor.cs`
- Create: `src/Clara.Core/Memory/SessionSummarizer.cs`
- Create: `src/Clara.Core/Memory/EmotionalContext.cs`
- Test: `tests/Clara.Core.Tests/Memory/MemoryExtractorTests.cs`

**Steps:**

1. MemoryExtractor uses LLM to extract facts from conversation messages. Takes ILlmProvider.

2. SessionSummarizer generates session summary on timeout.

3. EmotionalContext tracks sentiment across messages (basic positive/negative/neutral classification).

4. Write tests with mocked LLM provider.

5. Commit.

---

### Task 1.7: MarkdownMemoryView

**Files:**
- Create: `src/Clara.Core/Memory/MarkdownMemoryView.cs`
- Test: `tests/Clara.Core.Tests/Memory/MarkdownMemoryViewTests.cs`

**Steps:**

1. Implement IMemoryView — export memories to Markdown grouped by category, import from Markdown.

2. Test round-trip: export → import → verify.

3. Commit.

---

### Task 1.8: Prompt composition system

**Files:**
- Create: `src/Clara.Core/Prompt/IPromptSection.cs`
- Create: `src/Clara.Core/Prompt/PromptComposer.cs`
- Create: `src/Clara.Core/Prompt/PersonaSection.cs`
- Create: `src/Clara.Core/Prompt/ToolConventionsSection.cs`
- Create: `src/Clara.Core/Prompt/UserContextSection.cs`
- Create: `src/Clara.Core/Prompt/MemorySection.cs`
- Create: `src/Clara.Core/Prompt/SkillSection.cs`
- Create: `src/Clara.Core/Prompt/HeartbeatSection.cs`
- Test: `tests/Clara.Core.Tests/Prompt/PromptComposerTests.cs`

**Steps:**

1. IPromptSection interface:
```csharp
public interface IPromptSection
{
    string Name { get; }
    int Priority { get; } // lower = appears first in prompt
    Task<string?> GetContentAsync(PromptContext context, CancellationToken ct = default);
}

public record PromptContext(string SessionKey, string UserId, string Platform, string? WorkspaceDir);
```

2. PromptComposer collects all IPromptSection implementations, orders by priority, concatenates non-null results.

3. PersonaSection reads `workspace/persona.md`.

4. ToolConventionsSection reads `workspace/tools.md`.

5. UserContextSection reads `workspace/users/{userId}/user.md`.

6. MemorySection searches IMemoryStore for relevant memories and formats them.

7. Write tests with filesystem stubs.

8. Commit.

---

### Task 1.9: Session management

**Files:**
- Create: `src/Clara.Core/Sessions/SessionKey.cs`
- Create: `src/Clara.Core/Sessions/ISessionManager.cs`
- Create: `src/Clara.Core/Sessions/Session.cs`
- Create: `src/Clara.Core/Sessions/SessionManager.cs`
- Create: `src/Clara.Core/Sessions/SessionTimeoutPolicy.cs`
- Test: `tests/Clara.Core.Tests/Sessions/SessionKeyTests.cs`
- Test: `tests/Clara.Core.Tests/Sessions/SessionManagerTests.cs`

**Steps:**

1. SessionKey exactly as specified in §4.5 — parse/build structured keys like `clara:main:discord:dm:123456789`.

2. SessionManager — create, load (from DB), timeout, summarize. Uses IServiceScopeFactory for scoped DbContext.

3. SessionTimeoutPolicy — configurable idle timeout (default 30 min).

4. Write tests for SessionKey parsing (various formats) and SessionManager CRUD.

5. Commit.

---

### Task 1.10: Event bus

**Files:**
- Create: `src/Clara.Core/Events/IClaraEventBus.cs`
- Create: `src/Clara.Core/Events/ClaraEvent.cs`
- Create: `src/Clara.Core/Events/SessionEvents.cs`
- Create: `src/Clara.Core/Events/MessageEvents.cs`
- Create: `src/Clara.Core/Events/ToolEvents.cs`
- Create: `src/Clara.Core/Events/AdapterEvents.cs`
- Create: `src/Clara.Core/Events/ClaraEventBus.cs`
- Test: `tests/Clara.Core.Tests/Events/ClaraEventBusTests.cs`

**Steps:**

1. IClaraEventBus — typed pub/sub with priority ordering. Handlers run concurrently, errors isolated.

2. Event types as typed records inheriting from ClaraEvent.

3. Write tests: subscribe, publish, verify handler called. Test error isolation. Test priority ordering.

4. Commit.

---

### Task 1.11: DI registration for Clara.Core

**Files:**
- Create: `src/Clara.Core/ServiceCollectionExtensions.cs`

**Steps:**

1. Extension method `AddClaraCore(this IServiceCollection services, IConfiguration config)` that registers:
   - ClaraDbContext
   - LLM providers and factory
   - Memory store and view
   - Prompt composer and sections
   - Session manager
   - Event bus
   - All options classes bound from config

2. Verify build.

3. Commit.

---

## Phase 2: Tool System + Agentic Loop

### Task 2.1: Tool interfaces and registry

**Files:**
- Create: `src/Clara.Core/Tools/ITool.cs`
- Create: `src/Clara.Core/Tools/IToolRegistry.cs`
- Create: `src/Clara.Core/Tools/ToolDefinition.cs` (already partially done — expand)
- Create: `src/Clara.Core/Tools/ToolResult.cs`
- Create: `src/Clara.Core/Tools/ToolCategory.cs`
- Create: `src/Clara.Core/Tools/ToolExecutionContext.cs`
- Create: `src/Clara.Core/Tools/ToolRegistry.cs`
- Test: `tests/Clara.Core.Tests/Tools/ToolRegistryTests.cs`

**Steps:**

1. Implement interfaces exactly as specified in §4.3.

2. ToolRegistry — concurrent dictionary of ITool by name, with list-by-category support.

3. Write tests.

4. Commit.

---

### Task 2.2: Tool policy pipeline

**Files:**
- Create: `src/Clara.Core/Tools/ToolPolicy/IToolPolicy.cs`
- Create: `src/Clara.Core/Tools/ToolPolicy/ToolPolicyPipeline.cs`
- Create: `src/Clara.Core/Tools/ToolPolicy/ToolPolicyDecision.cs`
- Create: `src/Clara.Core/Tools/ToolPolicy/AgentToolPolicy.cs`
- Create: `src/Clara.Core/Tools/ToolPolicy/ChannelToolPolicy.cs`
- Create: `src/Clara.Core/Tools/ToolPolicy/SandboxToolPolicy.cs`
- Test: `tests/Clara.Core.Tests/Tools/ToolPolicyPipelineTests.cs`

**Steps:**

1. Implement exactly as §4.4. Cascading evaluation: first Deny wins, first Allow wins, default Allow.

2. AgentToolPolicy — per-agent allow/deny from config.

3. ChannelToolPolicy — per-channel restrictions (e.g., no shell in public Discord channels).

4. Write tests for each policy and the pipeline composition.

5. Commit.

---

### Task 2.3: ToolSelector — classify message to select tool categories

**Files:**
- Create: `src/Clara.Core/Tools/ToolSelector.cs`
- Test: `tests/Clara.Core.Tests/Tools/ToolSelectorTests.cs`

**Steps:**

1. Start with keyword/category matcher (not LLM-based — per migration plan §9, start simple). Map message keywords to ToolCategory values.

2. When confidence is low, fall back to "inject all."

3. Write tests.

4. Commit.

---

### Task 2.4: ToolLoopDetector

**Files:**
- Create: `src/Clara.Core/Llm/ToolCalling/ToolLoopDetector.cs`
- Create: `src/Clara.Core/Llm/ToolCalling/ToolCallParser.cs`
- Create: `src/Clara.Core/Llm/ToolCalling/ToolCallResult.cs`
- Test: `tests/Clara.Core.Tests/Llm/ToolLoopDetectorTests.cs`

**Steps:**

1. ToolLoopDetector tracks recent tool calls. Detects:
   - N identical calls (same name + same args)
   - Circular patterns (A→B→A→B)
   - Total round limit exceeded

2. Configurable limits from ToolOptions.

3. Write tests for each detection pattern.

4. Commit.

---

### Task 2.5: LlmOrchestrator — the agentic loop

**Files:**
- Create: `src/Clara.Core/Llm/LlmOrchestrator.cs`
- Create: `src/Clara.Core/Llm/OrchestratorEvent.cs`
- Test: `tests/Clara.Core.Tests/Llm/LlmOrchestratorTests.cs`

**Steps:**

1. Implement LlmOrchestrator exactly as shown in migration plan §2b. Key behavior:
   - Send LLM request with tools
   - Stream response
   - Intercept tool calls
   - Execute tool
   - Feed result back
   - Repeat until final text or limit hit
   - Yield OrchestratorEvent stream (TextDelta, ToolStarted, ToolCompleted, LoopDetected, MaxRoundsReached)

2. Write tests with mocked ILlmProvider and IToolRegistry:
   - Test: LLM returns text only → single TextDelta, done
   - Test: LLM returns tool call → tool executes → LLM returns text
   - Test: Loop detection triggers
   - Test: Max rounds reached

3. Commit.

---

### Task 2.6: Built-in tools

**Files:**
- Create: `src/Clara.Core/Tools/BuiltIn/ShellTool.cs`
- Create: `src/Clara.Core/Tools/BuiltIn/FileTools.cs`
- Create: `src/Clara.Core/Tools/BuiltIn/WebSearchTool.cs`
- Create: `src/Clara.Core/Tools/BuiltIn/WebFetchTool.cs`
- Create: `src/Clara.Core/Tools/BuiltIn/MemoryTools.cs`
- Create: `src/Clara.Core/Tools/BuiltIn/SessionTools.cs`
- Test: `tests/Clara.Core.Tests/Tools/BuiltIn/ShellToolTests.cs`
- Test: `tests/Clara.Core.Tests/Tools/BuiltIn/FileToolsTests.cs`

**Steps:**

1. Implement each tool implementing ITool with proper JSON schemas.

2. ShellTool — execute shell commands with timeout, capture stdout/stderr.

3. FileTools — read, write, list, delete files within workspace.

4. WebSearchTool — Tavily API integration.

5. WebFetchTool — HTTP GET, extract text content.

6. MemoryTools — search, view, export, import memories.

7. SessionTools — list sessions, get history.

8. Write tests for ShellTool (subprocess execution) and FileTools (filesystem ops in temp dir).

9. Commit.

---

### Task 2.7: Sandbox system

**Files:**
- Create: `src/Clara.Gateway/Sandbox/ISandboxProvider.cs`
- Create: `src/Clara.Gateway/Sandbox/DockerSandbox.cs`
- Create: `src/Clara.Gateway/Sandbox/SandboxManager.cs`
- Test: `tests/Clara.Gateway.Tests/Sandbox/SandboxManagerTests.cs`

**Steps:**

1. ISandboxProvider — execute code in isolated container, return stdout/stderr.

2. DockerSandbox — use Docker.DotNet to create container, execute, collect output, cleanup.

3. SandboxManager — auto-select sandbox backend based on config.

4. Write tests with mocked Docker client.

5. Commit.

---

## Phase 3: Gateway + Adapters

### Task 3.1: SignalR hub for adapter connections

**Files:**
- Create: `src/Clara.Gateway/Hubs/AdapterHub.cs`
- Create: `src/Clara.Gateway/Hubs/MonitorHub.cs`
- Create: `src/Clara.Gateway/Hubs/IAdapterClient.cs`

**Steps:**

1. AdapterHub — SignalR hub that adapters connect to. Methods:
   - `SendMessage(ClaraMessage message)` — adapter sends message to gateway
   - `Authenticate(string secret)` — optional auth
   - `Subscribe(string sessionKey)` — subscribe to responses for a session

2. IAdapterClient — client-side interface:
   - `ReceiveTextDelta(string sessionKey, string text)` — streaming text
   - `ReceiveToolStatus(string sessionKey, string toolName, string status)` — tool execution updates
   - `ReceiveComplete(string sessionKey)` — message complete

3. MonitorHub — for dashboard/monitoring connections.

4. Commit.

---

### Task 3.2: Lane queue system

**Files:**
- Create: `src/Clara.Gateway/Queues/LaneQueueManager.cs`
- Create: `src/Clara.Gateway/Queues/LaneQueueWorker.cs`
- Create: `src/Clara.Gateway/Queues/QueueMetrics.cs`
- Create: `src/Clara.Gateway/Queues/SessionMessage.cs`
- Test: `tests/Clara.Gateway.Tests/Queues/LaneQueueManagerTests.cs`

**Steps:**

1. LaneQueueManager — per-session-key bounded channels. Exactly as §2.3.

2. LaneQueueWorker — BackgroundService that drains lanes serially.

3. Write tests: enqueue to same lane → serial processing. Enqueue to different lanes → parallel.

4. Commit.

---

### Task 3.3: Message pipeline

**Files:**
- Create: `src/Clara.Gateway/Pipeline/IMessagePipeline.cs`
- Create: `src/Clara.Gateway/Pipeline/MessagePipeline.cs`
- Create: `src/Clara.Gateway/Pipeline/Stages/ContextBuildStage.cs`
- Create: `src/Clara.Gateway/Pipeline/Stages/ToolSelectionStage.cs`
- Create: `src/Clara.Gateway/Pipeline/Stages/LlmOrchestrationStage.cs`
- Create: `src/Clara.Gateway/Pipeline/Stages/ResponseRoutingStage.cs`
- Create: `src/Clara.Gateway/Pipeline/Middleware/RateLimitMiddleware.cs`
- Create: `src/Clara.Gateway/Pipeline/Middleware/StopPhraseMiddleware.cs`
- Create: `src/Clara.Gateway/Pipeline/Middleware/LoggingMiddleware.cs`
- Test: `tests/Clara.Gateway.Tests/Pipeline/MessagePipelineTests.cs`

**Steps:**

1. MessagePipeline orchestrates the stages in order:
   - Middleware (rate limit, stop phrase, logging)
   - ContextBuildStage (memory + session + persona + user context)
   - ToolSelectionStage (classify → inject relevant tools)
   - LlmOrchestrationStage (streaming + tool loop via LlmOrchestrator)
   - ResponseRoutingStage (back to adapter via SignalR)

2. StopPhraseMiddleware — checks for "Clara stop", "nevermind", etc.

3. Write tests with mocked stages.

4. Commit.

---

### Task 3.4: Hooks system

**Files:**
- Create: `src/Clara.Gateway/Hooks/HookRegistry.cs`
- Create: `src/Clara.Gateway/Hooks/HookExecutor.cs`
- Create: `src/Clara.Gateway/Hooks/HookDefinition.cs`
- Create: `workspace/hooks/hooks.yaml`
- Test: `tests/Clara.Gateway.Tests/Hooks/HookRegistryTests.cs`

**Steps:**

1. HookRegistry loads YAML hook definitions. Each hook maps an event type to a shell command.

2. HookExecutor spawns Process with CLARA_* environment variables and timeout.

3. Write tests.

4. Commit.

---

### Task 3.5: Scheduler service

**Files:**
- Create: `src/Clara.Gateway/Services/SchedulerService.cs`
- Create: `src/Clara.Gateway/Services/CronParser.cs`
- Create: `src/Clara.Gateway/Services/ScheduledTask.cs`
- Test: `tests/Clara.Gateway.Tests/Services/CronParserTests.cs`

**Steps:**

1. SchedulerService — BackgroundService with 100ms tick loop. Supports interval, cron, and one-shot tasks.

2. CronParser — 5-field parser (minute hour dom month dow). Support *, */N, N-M, N,M.

3. Write tests for CronParser matching various expressions.

4. Commit.

---

### Task 3.6: Background services

**Files:**
- Create: `src/Clara.Gateway/Services/SessionCleanupService.cs`
- Create: `src/Clara.Gateway/Services/MemoryConsolidationService.cs`

**Steps:**

1. SessionCleanupService — periodic check for idle sessions, trigger timeout + summarization.

2. MemoryConsolidationService — periodic memory maintenance (merge duplicates, prune low-score).

3. Commit.

---

### Task 3.7: REST API controllers

**Files:**
- Create: `src/Clara.Gateway/Api/HealthController.cs`
- Create: `src/Clara.Gateway/Api/SessionsController.cs`
- Create: `src/Clara.Gateway/Api/MemoriesController.cs`
- Create: `src/Clara.Gateway/Api/AdminController.cs`
- Create: `src/Clara.Gateway/Api/OAuthController.cs`

**Steps:**

1. Port the REST API endpoints. All JSON responses use snake_case.

2. HealthController — GET /health.

3. SessionsController — CRUD for sessions.

4. MemoriesController — list, search, stats.

5. Commit.

---

### Task 3.8: Program.cs — full gateway startup

**Files:**
- Create: `src/Clara.Gateway/Program.cs` (rewrite the template)

**Steps:**

1. Full Program.cs wiring:
   - Serilog bootstrap
   - AddClaraCore() for all core services
   - AddSignalR()
   - Configure Kestrel (configurable port)
   - Map SignalR hubs
   - Map API controllers
   - Register background services
   - Load hooks
   - Start scheduler

2. Verify:
```bash
dotnet build
```

3. Commit.

---

### Task 3.9: Discord adapter

**Files:**
- Create: `src/Clara.Adapters.Discord/Clara.Adapters.Discord.csproj`
- Create: `src/Clara.Adapters.Discord/DiscordAdapter.cs`
- Create: `src/Clara.Adapters.Discord/DiscordMessageMapper.cs`
- Create: `src/Clara.Adapters.Discord/DiscordResponseSender.cs`
- Create: `src/Clara.Adapters.Discord/DiscordImageHandler.cs`
- Create: `src/Clara.Adapters.Discord/DiscordSlashCommands.cs`
- Create: `src/Clara.Adapters.Discord/DiscordGatewayClient.cs`

**Steps:**

1. Create project with DSharpPlus dependency:
```bash
dotnet new console -n Clara.Adapters.Discord -o src/Clara.Adapters.Discord --framework net10.0
dotnet sln add src/Clara.Adapters.Discord
dotnet add src/Clara.Adapters.Discord package DSharpPlus
dotnet add src/Clara.Adapters.Discord reference src/Clara.Core
```

2. DiscordAdapter — DSharpPlus bot that connects to Discord and the gateway via SignalR.

3. DiscordMessageMapper — converts Discord message to ClaraMessage (the gateway's internal format).

4. DiscordResponseSender — handles streaming text to Discord (typing indicator, chunked messages).

5. DiscordImageHandler — resize + base64 for image attachments.

6. DiscordSlashCommands — /clara, /mcp slash commands.

7. DiscordGatewayClient — SignalR client connecting to Clara.Gateway.

8. Commit.

---

## Phase 4: MCP + Extended Tools

### Task 4.1: MCP client infrastructure

**Files:**
- Create: `src/Clara.Core/Tools/Mcp/IMcpClient.cs`
- Create: `src/Clara.Core/Tools/Mcp/McpStdioClient.cs`
- Create: `src/Clara.Core/Tools/Mcp/McpHttpClient.cs`
- Create: `src/Clara.Core/Tools/Mcp/McpServerManager.cs`
- Create: `src/Clara.Core/Tools/Mcp/McpInstaller.cs`
- Create: `src/Clara.Core/Tools/Mcp/McpOAuthHandler.cs`
- Create: `src/Clara.Core/Tools/Mcp/McpRegistryAdapter.cs`
- Test: `tests/Clara.Core.Tests/Tools/Mcp/McpClientTests.cs`

**Steps:**

1. IMcpClient — connect, list tools, call tool. JSON-RPC protocol.

2. McpStdioClient — spawn process, communicate via stdin/stdout JSON-RPC.

3. McpHttpClient — HTTP/SSE transport for hosted servers.

4. McpServerManager — lifecycle: start, stop, restart, health check.

5. McpInstaller — install from Smithery, npm, GitHub, Docker, local path.

6. McpOAuthHandler — OAuth flow for hosted servers.

7. McpRegistryAdapter — bridge MCP tools into IToolRegistry with namespace prefix.

8. Write tests with mocked process/HTTP.

9. Commit.

---

### Task 4.2: Extended built-in tools

**Files:**
- Create: `src/Clara.Core/Tools/BuiltIn/GitHubTools.cs`
- Create: `src/Clara.Core/Tools/BuiltIn/AzureDevOpsTools.cs`
- Create: `src/Clara.Core/Tools/BuiltIn/GoogleWorkspaceTools.cs`
- Create: `src/Clara.Core/Tools/BuiltIn/EmailTools.cs`

**Steps:**

1. GitHubTools — repos, issues, PRs, workflows, files, gists via GitHub REST API.

2. AzureDevOpsTools — repos, pipelines, work items.

3. GoogleWorkspaceTools — Sheets, Drive, Docs, Calendar with OAuth.

4. EmailTools — monitoring and alerts.

5. Commit.

---

## Phase 5: Sub-Agents + Heartbeat

### Task 5.1: Sub-agent system

**Files:**
- Create: `src/Clara.Core/SubAgents/ISubAgentManager.cs`
- Create: `src/Clara.Core/SubAgents/SubAgentRequest.cs`
- Create: `src/Clara.Core/SubAgents/SubAgentResult.cs`
- Create: `src/Clara.Core/SubAgents/SubAgentManager.cs`
- Test: `tests/Clara.Core.Tests/SubAgents/SubAgentManagerTests.cs`

**Steps:**

1. SubAgentManager — spawn new lane with own session key, configurable model tier.

2. Parent gets notification, sub-agent announces results back when complete.

3. Max children per agent (configurable, default 5).

4. Write tests.

5. Commit.

---

### Task 5.2: Cross-session tools

**Files:**
- Modify: `src/Clara.Core/Tools/BuiltIn/SessionTools.cs` (add cross-session methods)

**Steps:**

1. Add to SessionTools:
   - `sessions_list` — discover active sessions
   - `sessions_history` — get transcript from another session
   - `sessions_send` — send message to another session
   - `sessions_spawn` — spawn sub-agent

2. Commit.

---

### Task 5.3: Heartbeat service

**Files:**
- Create: `src/Clara.Gateway/Services/HeartbeatService.cs`
- Test: `tests/Clara.Gateway.Tests/Services/HeartbeatServiceTests.cs`

**Steps:**

1. HeartbeatService — BackgroundService that reads `workspace/heartbeat.md`, evaluates each checklist item via LLM, and sends messages to appropriate sessions when action needed.

2. Configurable interval (default 30 min).

3. Write tests with mocked LLM and filesystem.

4. Commit.

---

## Phase 6: Teams + CLI + Polish

### Task 6.1: Teams adapter

**Files:**
- Create: `src/Clara.Adapters.Teams/Clara.Adapters.Teams.csproj`
- Create: `src/Clara.Adapters.Teams/TeamsAdapter.cs`
- Create: `src/Clara.Adapters.Teams/TeamsMessageMapper.cs`
- Create: `src/Clara.Adapters.Teams/TeamsGatewayClient.cs`

**Steps:**

1. Create project:
```bash
dotnet new web -n Clara.Adapters.Teams -o src/Clara.Adapters.Teams --framework net10.0
dotnet sln add src/Clara.Adapters.Teams
dotnet add src/Clara.Adapters.Teams reference src/Clara.Core
```

2. TeamsAdapter — Bot Framework SDK integration.

3. SignalR client to gateway.

4. Commit.

---

### Task 6.2: CLI adapter

**Files:**
- Create: `src/Clara.Adapters.Cli/Clara.Adapters.Cli.csproj`
- Create: `src/Clara.Adapters.Cli/CliAdapter.cs`
- Create: `src/Clara.Adapters.Cli/CliRenderer.cs`
- Create: `src/Clara.Adapters.Cli/CliGatewayClient.cs`

**Steps:**

1. Create project:
```bash
dotnet new console -n Clara.Adapters.Cli -o src/Clara.Adapters.Cli --framework net10.0
dotnet sln add src/Clara.Adapters.Cli
dotnet add src/Clara.Adapters.Cli reference src/Clara.Core
dotnet add src/Clara.Adapters.Cli package Spectre.Console
```

2. CliAdapter — interactive terminal with Markdown rendering via Spectre.Console.

3. CliGatewayClient — SignalR client.

4. Commit.

---

### Task 6.3: Production hardening

**Files:**
- Modify: `src/Clara.Gateway/Program.cs` — add health checks, metrics
- Modify: `docker/Dockerfile` — optimize build
- Modify: `docker/docker-compose.yml` — add Redis, health checks

**Steps:**

1. Add health check endpoints with ASP.NET Core health checks.

2. Add Prometheus metrics endpoint.

3. Optimize Docker image (multi-stage, self-contained publish).

4. Commit.

---

### Task 6.4: CI/CD

**Files:**
- Create: `.github/workflows/ci.yml`

**Steps:**

1. GitHub Actions: build → test → Docker image.

2. Commit.

---

### Task 6.5: Update CLAUDE.md and documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/MyPalClara_DotNet_Migration_Plan.md` — mark status as "Implemented"

**Steps:**

1. Update CLAUDE.md to reflect new project structure, build commands, architecture.

2. Commit.

---

## Verification Checklist

After all tasks complete:

- [ ] `dotnet build` succeeds with 0 errors, 0 warnings
- [ ] `dotnet test` passes all tests
- [ ] `docker compose -f docker/docker-compose.yml config` validates
- [ ] Clara.Core has: LLM providers, memory, prompts, tools, sessions, events
- [ ] Clara.Gateway has: pipeline, queues, hubs, services, sandbox, hooks, scheduler, API
- [ ] Clara.Adapters.Discord connects via SignalR
- [ ] Clara.Adapters.Teams connects via SignalR
- [ ] Clara.Adapters.Cli provides interactive terminal
- [ ] MCP client can spawn stdio and HTTP servers
- [ ] Sub-agent system spawns into new lanes
- [ ] Heartbeat evaluates checklist periodically
- [ ] Tool policy pipeline enforces allow/deny
- [ ] Tool loop detector prevents runaway execution
- [ ] Workspace files (persona.md, tools.md, heartbeat.md) are loaded by prompt composer
