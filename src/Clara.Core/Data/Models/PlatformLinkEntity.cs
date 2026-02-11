using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Clara.Core.Data.Models;

[Table("platform_links")]
public class PlatformLinkEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public Guid UserId { get; set; }

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

    [Column("linked_via")]
    [MaxLength(50)]
    public string? LinkedVia { get; set; }

    [Column("linked_at")]
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public UserEntity? User { get; set; }
}
