namespace MyPalClara.Core.Processing;

public abstract record OrchestratorEvent
{
    public sealed record TextChunk(string Text) : OrchestratorEvent;
    public sealed record ToolStart(string Name, int Step) : OrchestratorEvent;
    public sealed record ToolEnd(string Name, bool Success, string Preview) : OrchestratorEvent;
    public sealed record Complete(string FullText, int ToolCount) : OrchestratorEvent;
}
