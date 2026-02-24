namespace MyPalClara.Memory.VectorStore;

public record MemoryPoint(
    string Id,
    string Data,
    string? Hash,
    string? UserId,
    string? AgentId,
    string? RunId,
    string? CreatedAt,
    string? UpdatedAt,
    bool IsKey,
    Dictionary<string, object>? Metadata);
