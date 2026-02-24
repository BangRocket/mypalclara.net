using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Tools;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

public class CoreToolsRegistrarTests
{
    [Fact]
    public void RegisterAll_Registers24PlusCoreTools()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        CoreToolsRegistrar.RegisterAll(registry, scopeFactory: null!, bridge: null!);

        var tools = registry.GetAllTools();
        Assert.True(tools.Count >= 24, $"Expected >= 24 tools, got {tools.Count}");
    }

    [Fact]
    public void RegisterAll_ToolNamesAreUnique()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        CoreToolsRegistrar.RegisterAll(registry, scopeFactory: null!, bridge: null!);

        var tools = registry.GetAllTools();
        var names = tools.Select(t => t.Name).ToList();
        Assert.Equal(names.Distinct().Count(), names.Count);
    }
}
