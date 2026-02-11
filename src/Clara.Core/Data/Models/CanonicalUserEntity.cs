using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Clara.Core.Data.Models;

/// <summary>Maps to canonical_users table — unified identity across platforms.</summary>
[Table("canonical_users")]
public class CanonicalUserEntity
{
    [Key]
    [Column("id")]
    [MaxLength(255)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("display_name")]
    [MaxLength(255)]
    public string DisplayName { get; set; } = "";

    [Column("primary_email")]
    [MaxLength(255)]
    public string? PrimaryEmail { get; set; }

    [Column("avatar_url")]
    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "active";

    [Column("is_admin")]
    public bool IsAdmin { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PlatformLinkEntity> PlatformLinks { get; set; } = [];
}
