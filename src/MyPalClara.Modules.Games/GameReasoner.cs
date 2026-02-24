using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Llm;

namespace MyPalClara.Modules.Games;

public class GameReasoner : IGameReasoner
{
    private readonly ILlmProvider _llm;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GameReasoner> _logger;
    private readonly string _railsApiUrl;

    public GameReasoner(ILlmProvider llm, HttpClient httpClient, ILogger<GameReasoner> logger)
    {
        _llm = llm;
        _httpClient = httpClient;
        _logger = logger;
        _railsApiUrl = Environment.GetEnvironmentVariable("CLARA_GATEWAY_API_URL")
            ?? "http://localhost:3000";
    }

    public async Task<string> ReasonMoveAsync(string gameId, string userId, CancellationToken ct = default)
    {
        // 1. Fetch game state from Rails BFF
        var gameState = await FetchGameStateAsync(gameId, ct);

        // 2. Build LLM prompt for move reasoning
        var messages = new LlmMessage[]
        {
            new SystemMessage("""
                You are a game-playing AI. Given the current game state, decide on the best move.
                Respond with JSON: {"move": "...", "reasoning": "..."}
                """),
            new UserMessage($"Game ID: {gameId}\nState:\n{gameState}")
        };

        var response = await _llm.InvokeAsync(messages, ct: ct);
        return response.Content ?? "{\"move\": \"pass\", \"reasoning\": \"Unable to determine move\"}";
    }

    public async Task<string> AnalyzeAsync(string gameId, string userId, CancellationToken ct = default)
    {
        var gameState = await FetchGameStateAsync(gameId, ct);

        var messages = new LlmMessage[]
        {
            new SystemMessage("""
                You are a game analysis AI. Analyze the current game state and provide strategic suggestions.
                Respond with JSON: {"analysis": "...", "suggestions": ["..."]}
                """),
            new UserMessage($"Game ID: {gameId}\nState:\n{gameState}")
        };

        var response = await _llm.InvokeAsync(messages, ct: ct);
        return response.Content ?? "{\"analysis\": \"Unable to analyze\", \"suggestions\": []}";
    }

    private async Task<string> FetchGameStateAsync(string gameId, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_railsApiUrl}/api/v1/games/{gameId}", ct);
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch game state for {GameId}", gameId);
            return "{}";
        }
    }
}
