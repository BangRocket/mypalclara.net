namespace MyPalClara.Data.Entities;

public class Intention
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string AgentId { get; set; } = "mypalclara";
    public string Content { get; set; } = null!;
    public string? SourceMemoryId { get; set; }
    public string TriggerConditions { get; set; } = null!;
    public int Priority { get; set; }
    public bool Fired { get; set; }
    public bool FireOnce { get; set; } = true;
    public DateTime? CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? FiredAt { get; set; }
}
