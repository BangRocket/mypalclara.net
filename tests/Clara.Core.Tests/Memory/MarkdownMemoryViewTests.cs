using Clara.Core.Data;
using Clara.Core.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clara.Core.Tests.Memory;

public class MarkdownMemoryViewTests
{
    private (ClaraDbContext Ctx, PgVectorMemoryStore Store, MarkdownMemoryView View) CreateSut()
    {
        var options = new DbContextOptionsBuilder<ClaraDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var ctx = new ClaraDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();

        var store = new PgVectorMemoryStore(ctx, NullLogger<PgVectorMemoryStore>.Instance);
        var view = new MarkdownMemoryView(store);

        return (ctx, store, view);
    }

    [Fact]
    public async Task ExportToMarkdown_NoMemories_ReturnsPlaceholder()
    {
        var (ctx, _, view) = CreateSut();
        using (ctx)
        {
            var md = await view.ExportToMarkdownAsync("user1");

            Assert.Contains("No memories stored", md);
        }
    }

    [Fact]
    public async Task ExportToMarkdown_GroupsByCategory()
    {
        var (ctx, store, view) = CreateSut();
        using (ctx)
        {
            await store.StoreAsync("user1", "Likes dogs", new MemoryMetadata("preferences"));
            await store.StoreAsync("user1", "Lives in Portland", new MemoryMetadata("personal"));
            await store.StoreAsync("user1", "Drinks coffee", new MemoryMetadata("preferences"));

            var md = await view.ExportToMarkdownAsync("user1");

            Assert.Contains("## preferences", md);
            Assert.Contains("## personal", md);
            Assert.Contains("- Likes dogs", md);
            Assert.Contains("- Lives in Portland", md);
            Assert.Contains("- Drinks coffee", md);
        }
    }

    [Fact]
    public async Task ExportToMarkdown_UncategorizedGroup()
    {
        var (ctx, store, view) = CreateSut();
        using (ctx)
        {
            await store.StoreAsync("user1", "Some fact");

            var md = await view.ExportToMarkdownAsync("user1");

            Assert.Contains("## Uncategorized", md);
            Assert.Contains("- Some fact", md);
        }
    }

    [Fact]
    public async Task ImportFromMarkdown_CreatesMemories()
    {
        var (ctx, store, view) = CreateSut();
        using (ctx)
        {
            var markdown = """
                # Memories

                ## preferences
                - Likes cats
                - Prefers tea

                ## personal
                - Lives in Seattle
                """;

            await view.ImportFromMarkdownAsync("user1", markdown);

            var all = await store.GetAllAsync("user1");
            Assert.Equal(3, all.Count);
            Assert.Contains(all, m => m.Content == "Likes cats" && m.Category == "preferences");
            Assert.Contains(all, m => m.Content == "Prefers tea" && m.Category == "preferences");
            Assert.Contains(all, m => m.Content == "Lives in Seattle" && m.Category == "personal");
        }
    }

    [Fact]
    public async Task GetReadable_ExistingMemory_ReturnsFormatted()
    {
        var (ctx, store, view) = CreateSut();
        using (ctx)
        {
            await store.StoreAsync("user1", "Favorite color is blue",
                new MemoryMetadata("preferences"));

            var all = await store.GetAllAsync("user1");
            var readable = await view.GetReadableAsync("user1", all[0].Id);

            Assert.NotNull(readable);
            Assert.Contains("Favorite color is blue", readable);
            Assert.Contains("preferences", readable);
            Assert.Contains(all[0].Id.ToString(), readable);
        }
    }

    [Fact]
    public async Task GetReadable_NonexistentMemory_ReturnsNull()
    {
        var (ctx, _, view) = CreateSut();
        using (ctx)
        {
            var readable = await view.GetReadableAsync("user1", Guid.NewGuid());

            Assert.Null(readable);
        }
    }
}
