using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyPalClara.Core.Data.Models;

[Table("memory_history")]
public class MemoryHistoryEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("memory_id")]
    [MaxLength(255)]
    public string MemoryId { get; set; } = "";

    [Column("old_memory")]
    public string? OldMemory { get; set; }

    [Column("new_memory")]
    public string? NewMemory { get; set; }

    [Column("event")]
    [MaxLength(50)]
    public string Event { get; set; } = "";

    [Column("user_id")]
    [MaxLength(255)]
    public string? UserId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
