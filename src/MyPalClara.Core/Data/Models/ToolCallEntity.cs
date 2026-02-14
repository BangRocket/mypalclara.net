using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyPalClara.Core.Data.Models;

[Table("tool_calls")]
public class ToolCallEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("conversation_id")]
    public Guid? ConversationId { get; set; }

    [Column("tool_name")]
    [MaxLength(255)]
    public string ToolName { get; set; } = "";

    [Column("arguments", TypeName = "jsonb")]
    public string Arguments { get; set; } = "{}";

    [Column("result")]
    public string? Result { get; set; }

    [Column("decision")]
    [MaxLength(50)]
    public string Decision { get; set; } = "allowed";

    [Column("latency_ms")]
    public int? LatencyMs { get; set; }

    [Column("success")]
    public bool Success { get; set; } = true;

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
