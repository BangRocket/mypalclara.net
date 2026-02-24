using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Tools;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Tools;

public class ProcessManagerTests
{
    private readonly ToolRegistry _registry = new(NullLogger<ToolRegistry>.Instance);
    private readonly ToolCallContext _ctx = new("user-1", "ch-1", "discord", "req-1");

    [Fact]
    public async Task ProcessList_EmptyInitially()
    {
        var pm = new ProcessManagerService();
        ProcessManagerTools.Register(_registry, pm);

        var result = await _registry.ExecuteAsync("process_list", new(), _ctx);

        Assert.True(result.Success);
        Assert.Contains("No", result.Output); // "No tracked processes"
    }

    [Fact]
    public async Task ProcessStart_And_ProcessList()
    {
        var pm = new ProcessManagerService();
        ProcessManagerTools.Register(_registry, pm);

        var args = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonDocument.Parse("\"sleep 60\"").RootElement
        };
        var startResult = await _registry.ExecuteAsync("process_start", args, _ctx);
        Assert.True(startResult.Success);

        var listResult = await _registry.ExecuteAsync("process_list", new(), _ctx);
        Assert.True(listResult.Success);
        Assert.Contains("sleep", listResult.Output);

        // Cleanup: stop the process
        var pid = JsonDocument.Parse(startResult.Output).RootElement.GetProperty("pid").GetString()!;
        var stopArgs = new Dictionary<string, JsonElement>
        {
            ["pid"] = JsonDocument.Parse($"\"{pid}\"").RootElement
        };
        await _registry.ExecuteAsync("process_stop", stopArgs, _ctx);
    }
}
