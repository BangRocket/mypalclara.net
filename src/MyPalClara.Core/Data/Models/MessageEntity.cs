using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyPalClara.Core.Data.Models;

[Table("messages")]
public class MessageEntity
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Column("conversation_id")]
    public Guid ConversationId { get; set; }

    [Column("user_id")]
    public Guid? UserId { get; set; }

    [Column("role")]
    [MaxLength(50)]
    public string Role { get; set; } = "user";

    [Column("content")]
    public string Content { get; set; } = "";

    [Column("metadata", TypeName = "jsonb")]
    public string? Metadata { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ConversationId))]
    public ConversationEntity? Conversation { get; set; }

    [ForeignKey(nameof(UserId))]
    public UserEntity? User { get; set; }
}
