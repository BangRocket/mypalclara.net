using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Clara.Core.Data.Models;

/// <summary>Maps to memory_supersessions table — tracks contradiction/update supersessions.</summary>
[Table("memory_supersessions")]
public class MemorySupersessionEntity
{
    [Key]
    [Column("id")]
    [MaxLength(255)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("old_memory_id")]
    [MaxLength(255)]
    public string OldMemoryId { get; set; } = "";

    [Column("new_memory_id")]
    [MaxLength(255)]
    public string NewMemoryId { get; set; } = "";

    [Column("user_id")]
    [MaxLength(255)]
    public string UserId { get; set; } = "";

    [Column("reason")]
    [MaxLength(100)]
    public string Reason { get; set; } = ""; // "contradiction", "update", "correction"

    [Column("confidence")]
    public double Confidence { get; set; }

    [Column("details")]
    public string? Details { get; set; } // JSON

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
