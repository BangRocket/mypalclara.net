using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Clara.Core.Data.Models;

/// <summary>Maps to memory_dynamics table — FSRS state for each memory.</summary>
[Table("memory_dynamics")]
public class MemoryDynamicsEntity
{
    [Key]
    [Column("memory_id")]
    [MaxLength(255)]
    public string MemoryId { get; set; } = "";

    [Column("user_id")]
    [MaxLength(255)]
    public string UserId { get; set; } = "";

    [Column("stability")]
    public double Stability { get; set; } = 1.0;

    [Column("difficulty")]
    public double Difficulty { get; set; } = 5.0;

    [Column("retrieval_strength")]
    public double RetrievalStrength { get; set; } = 1.0;

    [Column("storage_strength")]
    public double StorageStrength { get; set; } = 0.5;

    [Column("is_key")]
    public bool IsKey { get; set; }

    [Column("importance_weight")]
    public double ImportanceWeight { get; set; } = 1.0;

    [Column("category")]
    [MaxLength(50)]
    public string? Category { get; set; }

    [Column("tags")]
    public string? Tags { get; set; } // JSON array string

    [Column("last_accessed_at")]
    public DateTime? LastAccessedAt { get; set; }

    [Column("access_count")]
    public int AccessCount { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<MemoryAccessLogEntity> AccessLogs { get; set; } = [];
}
