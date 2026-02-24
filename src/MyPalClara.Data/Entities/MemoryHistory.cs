namespace MyPalClara.Data.Entities;

public class MemoryHistory
{
    public string Id { get; set; } = null!;
    public string MemoryId { get; set; } = null!;
    public string? OldMemory { get; set; }
    public string? NewMemory { get; set; }
    public string Event { get; set; } = null!;
    public bool IsDeleted { get; set; }
    public string? ActorId { get; set; }
    public string? Role { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
