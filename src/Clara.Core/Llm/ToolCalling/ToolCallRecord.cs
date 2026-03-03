namespace Clara.Core.Llm.ToolCalling;

public record ToolCallRecord(string Name, string ArgumentsHash, int Round);
