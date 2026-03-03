using Clara.Core.Data;
using Clara.Core.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clara.Core.Tests.Sessions;

public class SessionManagerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly SessionManager _manager;
    private readonly string _dbPath;

    public SessionManagerTests()
    {
        // Use a file-based SQLite DB so multiple scopes see the same data
        _dbPath = Path.Combine(Path.GetTempPath(), $"clara-test-{Guid.NewGuid()}.db");

        var services = new ServiceCollection();
        services.AddDbContext<ClaraDbContext>(opts => opts.UseSqlite($"DataSource={_dbPath}"));
        services.AddLogging();

        _provider = services.BuildServiceProvider();

        // Ensure DB is created
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();
        db.Database.EnsureCreated();

        _manager = new SessionManager(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SessionManager>.Instance);
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task GetOrCreate_CreatesNewSession()
    {
        var session = await _manager.GetOrCreateAsync("clara:main:discord:dm:123");

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal("active", session.Status);
        Assert.Equal("main", session.Key.AgentId);
        Assert.Equal("discord", session.Key.Platform);
    }

    [Fact]
    public async Task GetOrCreate_ReturnsSameSession()
    {
        var s1 = await _manager.GetOrCreateAsync("clara:main:discord:dm:456");
        var s2 = await _manager.GetOrCreateAsync("clara:main:discord:dm:456");

        Assert.Equal(s1.Id, s2.Id);
    }

    [Fact]
    public async Task Get_ExistingSession_ReturnsIt()
    {
        await _manager.GetOrCreateAsync("clara:main:cli:local:default");

        var session = await _manager.GetAsync("clara:main:cli:local:default");

        Assert.NotNull(session);
        Assert.Equal("active", session.Status);
    }

    [Fact]
    public async Task Get_NonexistentSession_ReturnsNull()
    {
        var session = await _manager.GetAsync("clara:main:cli:local:nonexistent");

        Assert.Null(session);
    }

    [Fact]
    public async Task Update_ChangesTitle()
    {
        var session = await _manager.GetOrCreateAsync("clara:main:discord:dm:789");
        session.Title = "Test Conversation";

        await _manager.UpdateAsync(session);

        var loaded = await _manager.GetAsync("clara:main:discord:dm:789");
        Assert.NotNull(loaded);
        Assert.Equal("Test Conversation", loaded.Title);
    }

    [Fact]
    public async Task Timeout_SetsStatusToTimeout()
    {
        await _manager.GetOrCreateAsync("clara:main:discord:dm:timeout");

        await _manager.TimeoutAsync("clara:main:discord:dm:timeout");

        var session = await _manager.GetAsync("clara:main:discord:dm:timeout");
        Assert.NotNull(session);
        Assert.Equal("timeout", session.Status);
    }

    [Fact]
    public async Task GetOrCreate_DoesNotReturnTimedOutSession()
    {
        var s1 = await _manager.GetOrCreateAsync("clara:main:discord:dm:reopen");
        await _manager.TimeoutAsync("clara:main:discord:dm:reopen");

        // Should create a NEW session since the old one is timed out
        var s2 = await _manager.GetOrCreateAsync("clara:main:discord:dm:reopen");

        Assert.NotEqual(s1.Id, s2.Id);
        Assert.Equal("active", s2.Status);
    }
}
