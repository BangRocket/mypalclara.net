using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyPalClara.Core.Data.Models;

[Table("discord_channel_details")]
public class DiscordChannelDetailEntity
{
    [Key]
    [Column("channel_id")]
    public Guid ChannelId { get; set; }

    [Column("guild_id")]
    public Guid GuildId { get; set; }

    [Column("category_name")]
    [MaxLength(255)]
    public string? CategoryName { get; set; }

    [Column("topic")]
    public string? Topic { get; set; }

    [Column("position")]
    public int? Position { get; set; }

    [Column("nsfw")]
    public bool Nsfw { get; set; }

    [ForeignKey(nameof(ChannelId))]
    public ChannelEntity? Channel { get; set; }

    [ForeignKey(nameof(GuildId))]
    public DiscordGuildEntity? Guild { get; set; }
}
