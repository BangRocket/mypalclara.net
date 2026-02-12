namespace MyPalClara.Core.Orchestration;

/// <summary>Events yielded by the orchestrator during generation.</summary>
public abstract record OrchestratorEvent;

/// <summary>A chunk of response text (for streaming).</summary>
public sealed record TextChunkEvent(string Text) : OrchestratorEvent;

/// <summary>A tool execution has started.</summary>
public sealed record ToolStartEvent(string ToolName, int Step) : OrchestratorEvent;

/// <summary>A tool execution has completed.</summary>
public sealed record ToolResultEvent(string ToolName, bool Success, string OutputPreview) : OrchestratorEvent;

/// <summary>The generation is complete.</summary>
public sealed record CompleteEvent(string FullText, int ToolCount) : OrchestratorEvent;
