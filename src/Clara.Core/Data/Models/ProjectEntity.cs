using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Clara.Core.Data.Models;

/// <summary>Maps to projects table — shared with Python Clara.</summary>
[Table("projects")]
public class ProjectEntity
{
    [Key]
    [Column("id")]
    [MaxLength(255)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("owner_id")]
    [MaxLength(255)]
    public string OwnerId { get; set; } = "";

    [Column("name")]
    [MaxLength(255)]
    public string Name { get; set; } = "";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SessionEntity> Sessions { get; set; } = [];
}
