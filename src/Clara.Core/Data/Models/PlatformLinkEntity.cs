using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Clara.Core.Data.Models;

/// <summary>Maps to platform_links table — links platform-specific user IDs to a canonical user.</summary>
[Table("platform_links")]
public class PlatformLinkEntity
{
    [Key]
    [Column("id")]
    [MaxLength(255)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("canonical_user_id")]
    [MaxLength(255)]
    public string CanonicalUserId { get; set; } = "";

    [Column("platform")]
    [MaxLength(50)]
    public string Platform { get; set; } = "";

    [Column("platform_user_id")]
    [MaxLength(255)]
    public string PlatformUserId { get; set; } = "";

    [Column("prefixed_user_id")]
    [MaxLength(255)]
    public string PrefixedUserId { get; set; } = "";

    [Column("display_name")]
    [MaxLength(255)]
    public string? DisplayName { get; set; }

    [Column("linked_at")]
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    [Column("linked_via")]
    [MaxLength(50)]
    public string? LinkedVia { get; set; }

    [ForeignKey(nameof(CanonicalUserId))]
    public CanonicalUserEntity? CanonicalUser { get; set; }
}
