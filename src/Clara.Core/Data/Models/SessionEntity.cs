using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Clara.Core.Data.Models;

/// <summary>Maps to sessions table — shared with Python Clara. Note: archived is string for Python compatibility.</summary>
[Table("sessions")]
public class SessionEntity
{
    [Key]
    [Column("id")]
    [MaxLength(255)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("user_id")]
    [MaxLength(255)]
    public string UserId { get; set; } = "";

    [Column("project_id")]
    [MaxLength(255)]
    public string ProjectId { get; set; } = "";

    [Column("context_id")]
    [MaxLength(255)]
    public string ContextId { get; set; } = "default";

    [Column("previous_session_id")]
    [MaxLength(255)]
    public string? PreviousSessionId { get; set; }

    /// <summary>String "true"/"false" — Python compatibility, NOT a bool.</summary>
    [Column("archived")]
    [MaxLength(10)]
    public string Archived { get; set; } = "false";

    [Column("session_summary")]
    public string? SessionSummary { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_activity_at")]
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ProjectId))]
    public ProjectEntity? Project { get; set; }

    public ICollection<MessageEntity> Messages { get; set; } = [];
}
