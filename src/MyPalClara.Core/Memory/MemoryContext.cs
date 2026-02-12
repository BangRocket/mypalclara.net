namespace MyPalClara.Core.Memory;

/// <summary>Assembled memory context from all subsystems.</summary>
public sealed class MemoryContext
{
    public List<MemoryItem> KeyMemories { get; init; } = [];
    public List<MemoryItem> RelevantMemories { get; init; } = [];
    public List<string> GraphRelations { get; init; } = [];
    public List<string> EmotionalContext { get; init; } = [];
    public List<string> RecurringTopics { get; init; } = [];
}
