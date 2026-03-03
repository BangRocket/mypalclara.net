using Clara.Core.Data;
using Clara.Core.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clara.Core.Tests.Memory;

public class PgVectorMemoryStoreTests
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
    public async Task StoreAndGetAll_RoundTrips()
    {
        using var ctx = CreateContext();
        var store = new PgVectorMemoryStore(ctx, NullLogger<PgVectorMemoryStore>.Instance);

        await store.StoreAsync("user1", "Clara likes dogs");
        await store.StoreAsync("user1", "Clara lives in Portland");

        var all = await store.GetAllAsync("user1");

        Assert.Equal(2, all.Count);
        Assert.Contains(all, m => m.Content == "Clara likes dogs");
        Assert.Contains(all, m => m.Content == "Clara lives in Portland");
    }

    [Fact]
    public async Task StoreWithMetadata_PreservesCategory()
    {
        using var ctx = CreateContext();
        var store = new PgVectorMemoryStore(ctx, NullLogger<PgVectorMemoryStore>.Instance);

        await store.StoreAsync("user1", "Favorite color is blue",
            new MemoryMetadata(Category: "preferences"));

        var all = await store.GetAllAsync("user1");

        Assert.Single(all);
        Assert.Equal("preferences", all[0].Category);
    }

    [Fact]
    public async Task Delete_RemovesMemory()
    {
        using var ctx = CreateContext();
        var store = new PgVectorMemoryStore(ctx, NullLogger<PgVectorMemoryStore>.Instance);

        await store.StoreAsync("user1", "To be deleted");
        var all = await store.GetAllAsync("user1");
        Assert.Single(all);

        await store.DeleteAsync("user1", all[0].Id);

        var afterDelete = await store.GetAllAsync("user1");
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task Delete_OnlyAffectsMatchingUser()
    {
        using var ctx = CreateContext();
        var store = new PgVectorMemoryStore(ctx, NullLogger<PgVectorMemoryStore>.Instance);

        await store.StoreAsync("user1", "User1 memory");
        await store.StoreAsync("user2", "User2 memory");

        var user1Memories = await store.GetAllAsync("user1");
        // Try to delete user1's memory using user2's userId
        await store.DeleteAsync("user2", user1Memories[0].Id);

        // Both should still exist
        Assert.Single(await store.GetAllAsync("user1"));
        Assert.Single(await store.GetAllAsync("user2"));
    }

    [Fact]
    public async Task Search_FindsMatchingContent()
    {
        using var ctx = CreateContext();
        var store = new PgVectorMemoryStore(ctx, NullLogger<PgVectorMemoryStore>.Instance);

        await store.StoreAsync("user1", "Clara enjoys hiking in the mountains");
        await store.StoreAsync("user1", "Clara's birthday is in March");
        await store.StoreAsync("user1", "Clara likes mountain biking");

        var results = await store.SearchAsync("user1", "mountain");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("mountain", r.Entry.Content, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAll_IsolatedByUser()
    {
        using var ctx = CreateContext();
        var store = new PgVectorMemoryStore(ctx, NullLogger<PgVectorMemoryStore>.Instance);

        await store.StoreAsync("user1", "User1 only");
        await store.StoreAsync("user2", "User2 only");

        var user1 = await store.GetAllAsync("user1");
        var user2 = await store.GetAllAsync("user2");

        Assert.Single(user1);
        Assert.Equal("User1 only", user1[0].Content);
        Assert.Single(user2);
        Assert.Equal("User2 only", user2[0].Content);
    }
}
