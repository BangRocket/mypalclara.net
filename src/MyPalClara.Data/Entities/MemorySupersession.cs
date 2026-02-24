namespace MyPalClara.Data.Entities;

public class MemorySupersession
{
    public string Id { get; set; } = null!;
    public string OldMemoryId { get; set; } = null!;
    public string NewMemoryId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? Reason { get; set; }
    public double Confidence { get; set; } = 1.0;
    public string? Details { get; set; }
    public DateTime? CreatedAt { get; set; }
}
