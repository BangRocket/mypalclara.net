using Clara.Core.Data;
using Clara.Core.Data.Entities;
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
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task CanCreateAndQueryUser()
    {
        using var ctx = CreateContext();
        ctx.Users.Add(new()
        {
            Id = Guid.NewGuid(),
            PlatformId = "123",
            Platform = "discord",
            DisplayName = "Test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        Assert.Single(await ctx.Users.ToListAsync());
    }

    [Fact]
    public async Task SessionHasMessages()
    {
        using var ctx = CreateContext();
        var session = new SessionEntity
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

    [Fact]
    public async Task CanCrudAllEntities()
    {
        using var ctx = CreateContext();

        ctx.Projects.Add(new()
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        ctx.McpServers.Add(new()
        {
            Id = Guid.NewGuid(),
            Name = "test-mcp",
            Command = "node server.js",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        ctx.EmailAccounts.Add(new()
        {
            Id = Guid.NewGuid(),
            Email = "test@test.com",
            ImapHost = "imap.test.com",
            CreatedAt = DateTime.UtcNow
        });
        ctx.ToolUsages.Add(new()
        {
            Id = Guid.NewGuid(),
            ToolName = "shell",
            SessionKey = "clara:main:cli:local:default",
            CreatedAt = DateTime.UtcNow
        });

        await ctx.SaveChangesAsync();

        Assert.Single(await ctx.Projects.ToListAsync());
        Assert.Single(await ctx.McpServers.ToListAsync());
        Assert.Single(await ctx.EmailAccounts.ToListAsync());
        Assert.Single(await ctx.ToolUsages.ToListAsync());
    }
}
