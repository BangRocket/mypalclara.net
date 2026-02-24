using System.Text.Json;
using MyPalClara.Modules.Games;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Core.Tests.Games;

public class GameToolSourceTests
{
    [Fact]
    public void GetTools_Returns2Tools()
    {
        var source = new GameToolSource(new FakeReasoner());
        var tools = source.GetTools();
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "game_make_move");
        Assert.Contains(tools, t => t.Name == "game_analyze");
    }

    [Fact]
    public void CanHandle_RecognizesGameTools()
    {
        var source = new GameToolSource(new FakeReasoner());
        Assert.True(source.CanHandle("game_make_move"));
        Assert.True(source.CanHandle("game_analyze"));
        Assert.False(source.CanHandle("other_tool"));
    }

    [Fact]
    public async Task ExecuteAsync_MakeMove_ReturnsResult()
    {
        var source = new GameToolSource(new FakeReasoner());
        var ctx = new ToolCallContext("u1", "c1", "discord", "r1");
        var args = new Dictionary<string, JsonElement>
        {
            ["game_id"] = JsonDocument.Parse("\"g1\"").RootElement
        };

        var result = await source.ExecuteAsync("game_make_move", args, ctx);
        Assert.True(result.Success);
        Assert.Contains("move", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    private class FakeReasoner : IGameReasoner
    {
        public Task<string> ReasonMoveAsync(string gameId, string userId, CancellationToken ct = default)
            => Task.FromResult("{\"move\": \"e2e4\", \"reasoning\": \"Opening with king pawn\"}");

        public Task<string> AnalyzeAsync(string gameId, string userId, CancellationToken ct = default)
            => Task.FromResult("{\"analysis\": \"Even position\", \"suggestions\": [\"Consider castling\"]}");
    }
}
