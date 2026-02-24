namespace MyPalClara.Data.Entities;

public class MemoryDynamics
{
    public string MemoryId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public double Stability { get; set; } = 1.0;
    public double Difficulty { get; set; } = 5.0;
    public double RetrievalStrength { get; set; } = 1.0;
    public double StorageStrength { get; set; } = 0.5;
    public bool IsKey { get; set; }
    public double ImportanceWeight { get; set; } = 1.0;
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<MemoryAccessLog> AccessLogs { get; set; } = [];
}
