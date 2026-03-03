namespace Clara.Core.SubAgents;

public record SubAgentResult(
    string SubTaskId,
    bool Success,
    string Content,
    string? Error = null);
