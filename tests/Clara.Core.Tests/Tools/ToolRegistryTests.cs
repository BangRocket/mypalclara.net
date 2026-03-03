using System.Text.Json;
using Clara.Core.Tools;

namespace Clara.Core.Tests.Tools;

public class ToolRegistryTests
{
    private static ITool CreateStubTool(string name, ToolCategory category = ToolCategory.Shell) =>
        new StubTool(name, category);

    [Fact]
    public void Register_and_resolve_by_name()
    {
        var registry = new ToolRegistry();
        var tool = CreateStubTool("shell_execute");

        registry.Register(tool);

        var resolved = registry.Resolve("shell_execute");
        Assert.NotNull(resolved);
        Assert.Equal("shell_execute", resolved.Name);
    }

    [Fact]
    public void Resolve_returns_null_for_unknown()
    {
        var registry = new ToolRegistry();

        var resolved = registry.Resolve("nonexistent");

        Assert.Null(resolved);
    }

    [Fact]
    public void GetByCategory_returns_matching_tools()
    {
        var registry = new ToolRegistry();
        registry.Register(CreateStubTool("shell_execute", ToolCategory.Shell));
        registry.Register(CreateStubTool("file_read", ToolCategory.FileSystem));
        registry.Register(CreateStubTool("file_write", ToolCategory.FileSystem));

        var fileTools = registry.GetByCategory(ToolCategory.FileSystem);

        Assert.Equal(2, fileTools.Count);
        Assert.All(fileTools, t => Assert.Equal(ToolCategory.FileSystem, t.Category));
    }

    [Fact]
    public void GetByCategory_returns_empty_for_no_match()
    {
        var registry = new ToolRegistry();
        registry.Register(CreateStubTool("shell_execute", ToolCategory.Shell));

        var webTools = registry.GetByCategory(ToolCategory.Web);

        Assert.Empty(webTools);
    }

    [Fact]
    public void GetAll_returns_all_registered_tools()
    {
        var registry = new ToolRegistry();
        registry.Register(CreateStubTool("a", ToolCategory.Shell));
        registry.Register(CreateStubTool("b", ToolCategory.Web));
        registry.Register(CreateStubTool("c", ToolCategory.Memory));

        var all = registry.GetAll();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void Register_overwrites_existing_tool_with_same_name()
    {
        var registry = new ToolRegistry();
        var tool1 = CreateStubTool("shell_execute", ToolCategory.Shell);
        var tool2 = CreateStubTool("shell_execute", ToolCategory.CodeExecution);

        registry.Register(tool1);
        registry.Register(tool2);

        var resolved = registry.Resolve("shell_execute");
        Assert.NotNull(resolved);
        Assert.Equal(ToolCategory.CodeExecution, resolved.Category);
    }

    private class StubTool : ITool
    {
        public StubTool(string name, ToolCategory category)
        {
            Name = name;
            Category = category;
        }

        public string Name { get; }
        public string Description => $"Stub tool: {Name}";
        public ToolCategory Category { get; }
        public JsonElement ParameterSchema => JsonDocument.Parse("{}").RootElement;

        public Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Ok("stub"));
    }
}
