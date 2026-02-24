using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Games;

public class GameToolSource : IToolSource
{
    private readonly IGameReasoner _reasoner;

    public GameToolSource(IGameReasoner reasoner) => _reasoner = reasoner;

    public string Name => "games";

    public IReadOnlyList<ToolSchema> GetTools() =>
    [
        new ToolSchema("game_make_move",
            "Reason about the current game state and decide on the best move. Args: game_id (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"game_id":{"type":"string"}},"required":["game_id"]}""").RootElement),
        new ToolSchema("game_analyze",
            "Analyze a game state and suggest strategy. Args: game_id (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"game_id":{"type":"string"}},"required":["game_id"]}""").RootElement)
    ];

    public bool CanHandle(string toolName) => toolName is "game_make_move" or "game_analyze";

    public async Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default)
    {
        var gameId = args.TryGetValue("game_id", out var gElem) ? gElem.GetString()! : "";

        switch (toolName)
        {
            case "game_make_move":
            {
                var result = await _reasoner.ReasonMoveAsync(gameId, context.UserId, ct);
                return new ToolResult(true, result);
            }
            case "game_analyze":
            {
                var result = await _reasoner.AnalyzeAsync(gameId, context.UserId, ct);
                return new ToolResult(true, result);
            }
            default:
                return new ToolResult(false, "", $"Unknown game tool: {toolName}");
        }
    }
}
