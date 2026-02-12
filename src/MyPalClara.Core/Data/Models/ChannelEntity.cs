using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyPalClara.Core.Data.Models;

[Table("channels")]
public class ChannelEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("adapter_id")]
    public Guid AdapterId { get; set; }

    [Column("external_id")]
    [MaxLength(255)]
    public string ExternalId { get; set; } = "";

    [Column("name")]
    [MaxLength(255)]
    public string Name { get; set; } = "";

    [Column("channel_type")]
    [MaxLength(50)]
    public string ChannelType { get; set; } = "text";

    [Column("metadata", TypeName = "jsonb")]
    public string? Metadata { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(AdapterId))]
    public AdapterEntity? Adapter { get; set; }

    public ICollection<ConversationEntity> Conversations { get; set; } = [];
}
