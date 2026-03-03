using Clara.Core.Prompt;

namespace Clara.Core.Tests.Prompt;

public class PromptComposerTests
{
    private class TestSection : IPromptSection
    {
        public string Name { get; }
        public int Priority { get; }
        private readonly string? _content;

        public TestSection(string name, int priority, string? content)
        {
            Name = name;
            Priority = priority;
            _content = content;
        }

        public Task<string?> GetContentAsync(PromptContext context, CancellationToken ct = default)
            => Task.FromResult(_content);
    }

    [Fact]
    public async Task Compose_OrdersByPriority()
    {
        var sections = new IPromptSection[]
        {
            new TestSection("second", 200, "Second content"),
            new TestSection("first", 100, "First content"),
            new TestSection("third", 300, "Third content"),
        };

        var composer = new PromptComposer(sections);
        var ctx = new PromptContext("session1", "user1", "test", null);

        var result = await composer.ComposeAsync(ctx);

        var firstIdx = result.IndexOf("<first>", StringComparison.Ordinal);
        var secondIdx = result.IndexOf("<second>", StringComparison.Ordinal);
        var thirdIdx = result.IndexOf("<third>", StringComparison.Ordinal);

        Assert.True(firstIdx < secondIdx, "First section should appear before second");
        Assert.True(secondIdx < thirdIdx, "Second section should appear before third");
    }

    [Fact]
    public async Task Compose_SkipsNullSections()
    {
        var sections = new IPromptSection[]
        {
            new TestSection("visible", 100, "Visible content"),
            new TestSection("hidden", 200, null),
            new TestSection("also-visible", 300, "Also visible"),
        };

        var composer = new PromptComposer(sections);
        var ctx = new PromptContext("session1", "user1", "test", null);

        var result = await composer.ComposeAsync(ctx);

        Assert.Contains("<visible>", result);
        Assert.DoesNotContain("<hidden>", result);
        Assert.Contains("<also-visible>", result);
    }

    [Fact]
    public async Task Compose_SkipsEmptySections()
    {
        var sections = new IPromptSection[]
        {
            new TestSection("visible", 100, "Visible"),
            new TestSection("empty", 200, "   "),
        };

        var composer = new PromptComposer(sections);
        var ctx = new PromptContext("session1", "user1", "test", null);

        var result = await composer.ComposeAsync(ctx);

        Assert.Contains("<visible>", result);
        Assert.DoesNotContain("<empty>", result);
    }

    [Fact]
    public async Task Compose_IncludesContent()
    {
        var sections = new IPromptSection[]
        {
            new TestSection("persona", 0, "You are Clara, a helpful AI assistant."),
        };

        var composer = new PromptComposer(sections);
        var ctx = new PromptContext("session1", "user1", "test", null);

        var result = await composer.ComposeAsync(ctx);

        Assert.Contains("You are Clara, a helpful AI assistant.", result);
    }

    [Fact]
    public async Task PersonaSection_ReadsFromFile()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "clara-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tmpDir);
        try
        {
            var personaPath = Path.Combine(tmpDir, "persona.md");
            await File.WriteAllTextAsync(personaPath, "You are Clara.");

            var section = new PersonaSection();
            var ctx = new PromptContext("session1", "user1", "test", tmpDir);

            var content = await section.GetContentAsync(ctx);

            Assert.Equal("You are Clara.", content);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task PersonaSection_ReturnsNull_WhenNoFile()
    {
        var section = new PersonaSection();
        var ctx = new PromptContext("session1", "user1", "test", "/nonexistent");

        var content = await section.GetContentAsync(ctx);

        Assert.Null(content);
    }

    [Fact]
    public async Task PersonaSection_ReturnsNull_WhenNoWorkspace()
    {
        var section = new PersonaSection();
        var ctx = new PromptContext("session1", "user1", "test", null);

        var content = await section.GetContentAsync(ctx);

        Assert.Null(content);
    }
}
