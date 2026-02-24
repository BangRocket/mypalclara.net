using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Tools;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

/// <summary>
/// ChatHistoryTools requires a DB context, so these tests verify registration only.
/// Full integration tests require EF InMemory or SQLite.
/// </summary>
public class ChatHistoryToolsTests
{
    private readonly ToolRegistry _registry = new(NullLogger<ToolRegistry>.Instance);

    [Fact]
    public void Register_AddsTools()
    {
        // ChatHistoryTools.Register takes IServiceScopeFactory; pass null and verify tools are listed
        ChatHistoryTools.Register(_registry, null!);

        var tools = _registry.GetAllTools();
        var names = tools.Select(t => t.Name).ToList();
        Assert.Contains("search_chat_history", names);
        Assert.Contains("get_chat_history", names);
    }
}
