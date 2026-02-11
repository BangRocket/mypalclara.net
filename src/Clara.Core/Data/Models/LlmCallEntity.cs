using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Clara.Core.Data.Models;

[Table("llm_calls")]
public class LlmCallEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("conversation_id")]
    public Guid? ConversationId { get; set; }

    [Column("model")]
    [MaxLength(255)]
    public string Model { get; set; } = "";

    [Column("provider")]
    [MaxLength(50)]
    public string Provider { get; set; } = "";

    [Column("request_body", TypeName = "jsonb")]
    public string RequestBody { get; set; } = "{}";

    [Column("response_body", TypeName = "jsonb")]
    public string ResponseBody { get; set; } = "{}";

    [Column("input_tokens")]
    public int? InputTokens { get; set; }

    [Column("output_tokens")]
    public int? OutputTokens { get; set; }

    [Column("cache_read_tokens")]
    public int? CacheReadTokens { get; set; }

    [Column("cache_write_tokens")]
    public int? CacheWriteTokens { get; set; }

    [Column("latency_ms")]
    public int? LatencyMs { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "success";

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ConversationId))]
    public ConversationEntity? Conversation { get; set; }
}
