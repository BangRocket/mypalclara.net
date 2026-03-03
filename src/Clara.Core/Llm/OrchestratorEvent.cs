using Clara.Core.Tools;

namespace Clara.Core.Llm;

public abstract record OrchestratorEvent;
public record TextDelta(string Text) : OrchestratorEvent;
public record ToolStarted(string ToolName, string Arguments) : OrchestratorEvent;
public record ToolCompleted(string ToolName, ToolResult Result) : OrchestratorEvent;
public record LoopDetected(string ToolName, int Round) : OrchestratorEvent;
public record MaxRoundsReached(int MaxRounds) : OrchestratorEvent;
