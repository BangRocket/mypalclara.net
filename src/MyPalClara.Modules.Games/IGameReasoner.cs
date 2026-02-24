namespace MyPalClara.Modules.Games;

public interface IGameReasoner
{
    Task<string> ReasonMoveAsync(string gameId, string userId, CancellationToken ct = default);
    Task<string> AnalyzeAsync(string gameId, string userId, CancellationToken ct = default);
}
