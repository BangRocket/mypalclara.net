using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Clara.Core.Data.Models;

/// <summary>Maps to memory_access_log table — tracks each FSRS review event.</summary>
[Table("memory_access_log")]
public class MemoryAccessLogEntity
{
    [Key]
    [Column("id")]
    [MaxLength(255)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("memory_id")]
    [MaxLength(255)]
    public string MemoryId { get; set; } = "";

    [Column("user_id")]
    [MaxLength(255)]
    public string UserId { get; set; } = "";

    [Column("grade")]
    public int Grade { get; set; }

    [Column("signal_type")]
    [MaxLength(100)]
    public string SignalType { get; set; } = "";

    [Column("retrievability_at_access")]
    public double RetrievabilityAtAccess { get; set; }

    [Column("context")]
    public string? Context { get; set; } // Optional JSON

    [Column("accessed_at")]
    public DateTime AccessedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(MemoryId))]
    public MemoryDynamicsEntity? MemoryDynamics { get; set; }
}
