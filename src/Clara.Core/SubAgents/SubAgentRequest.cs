using Clara.Core.Llm;

namespace Clara.Core.SubAgents;

public record SubAgentRequest(
    string Task,
    string ParentSessionKey,
    ModelTier Tier = ModelTier.Low,
    int TimeoutMinutes = 10);
