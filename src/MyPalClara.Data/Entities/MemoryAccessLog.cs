namespace MyPalClara.Data.Entities;

public class MemoryAccessLog
{
    public string Id { get; set; } = null!;
    public string MemoryId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public int Grade { get; set; }
    public string? SignalType { get; set; }
    public double? RetrievabilityAtAccess { get; set; }
    public string? Context { get; set; }
    public DateTime? AccessedAt { get; set; }

    public MemoryDynamics Memory { get; set; } = null!;
}
