using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyPalClara.Core.Data.Models;

[Table("conversations")]
public class ConversationEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("channel_id")]
    public Guid ChannelId { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("previous_conversation_id")]
    public Guid? PreviousConversationId { get; set; }

    [Column("summary")]
    public string? Summary { get; set; }

    [Column("started_at")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [Column("last_activity_at")]
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    [Column("archived")]
    public bool Archived { get; set; }

    [ForeignKey(nameof(ChannelId))]
    public ChannelEntity? Channel { get; set; }

    [ForeignKey(nameof(UserId))]
    public UserEntity? User { get; set; }

    [ForeignKey(nameof(PreviousConversationId))]
    public ConversationEntity? PreviousConversation { get; set; }

    public ICollection<MessageEntity> Messages { get; set; } = [];
}
