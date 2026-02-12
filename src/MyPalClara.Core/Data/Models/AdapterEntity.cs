using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyPalClara.Core.Data.Models;

[Table("adapters")]
public class AdapterEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("type")]
    [MaxLength(50)]
    public string Type { get; set; } = "";

    [Column("name")]
    [MaxLength(255)]
    public string Name { get; set; } = "";

    [Column("config", TypeName = "jsonb")]
    public string? Config { get; set; }

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ChannelEntity> Channels { get; set; } = [];
}
