namespace Clara.Core.Data.Entities;

public class SessionEntity
{
    public Guid Id { get; set; }
    public string SessionKey { get; set; } = "";
    public Guid? UserId { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = "active";
    public string? Summary { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public UserEntity? User { get; set; }
    public List<MessageEntity> Messages { get; set; } = [];
}
