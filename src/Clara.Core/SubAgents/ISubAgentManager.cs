namespace Clara.Core.SubAgents;

public interface ISubAgentManager
{
    Task<string> SpawnAsync(SubAgentRequest request, CancellationToken ct = default);
    Task<SubAgentResult?> GetResultAsync(string subTaskId, CancellationToken ct = default);
    IReadOnlyList<string> GetActiveSubAgents(string parentSessionKey);
    Task CancelAsync(string subTaskId, CancellationToken ct = default);
}
