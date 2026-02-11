using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Clara.Core.Data.Models;

/// <summary>Maps to messages table — shared with Python Clara. Only entity with int auto-increment PK.</summary>
[Table("messages")]
public class MessageEntity
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Column("session_id")]
    [MaxLength(255)]
    public string SessionId { get; set; } = "";

    [Column("user_id")]
    [MaxLength(255)]
    public string UserId { get; set; } = "";

    [Column("role")]
    [MaxLength(50)]
    public string Role { get; set; } = "user";

    [Column("content")]
    public string Content { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(SessionId))]
    public SessionEntity? Session { get; set; }
}
