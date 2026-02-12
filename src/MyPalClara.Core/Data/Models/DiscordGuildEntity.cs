using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyPalClara.Core.Data.Models;

[Table("discord_guilds")]
public class DiscordGuildEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("adapter_id")]
    public Guid AdapterId { get; set; }

    [Column("guild_id")]
    [MaxLength(255)]
    public string GuildId { get; set; } = "";

    [Column("name")]
    [MaxLength(255)]
    public string Name { get; set; } = "";

    [Column("icon_url")]
    [MaxLength(500)]
    public string? IconUrl { get; set; }

    [Column("owner_id")]
    [MaxLength(255)]
    public string? OwnerId { get; set; }

    [Column("member_count")]
    public int? MemberCount { get; set; }

    [Column("synced_at")]
    public DateTime? SyncedAt { get; set; }

    [ForeignKey(nameof(AdapterId))]
    public AdapterEntity? Adapter { get; set; }
}
