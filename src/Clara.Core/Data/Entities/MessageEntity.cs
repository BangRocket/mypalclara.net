namespace Clara.Core.Data.Entities;

public class MessageEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string? ToolCalls { get; set; }
    public string? ToolResults { get; set; }
    public string? Metadata { get; set; }
    public int TokenCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public SessionEntity? Session { get; set; }
}
