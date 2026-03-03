using Clara.Core.Llm;

namespace Clara.Core.Sessions;

public class Session
{
    public Guid Id { get; set; }
    public SessionKey Key { get; set; } = null!;
    public string? UserId { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = "active";
    public List<LlmMessage> Messages { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
}
