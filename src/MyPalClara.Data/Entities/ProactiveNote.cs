namespace MyPalClara.Data.Entities;

public class ProactiveNote
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string Note { get; set; } = null!;
    public string? NoteType { get; set; }
    public string? SourceContext { get; set; }
    public string? SourceModel { get; set; }
    public string? SourceConfidence { get; set; }
    public string? GroundingMessageIds { get; set; }
    public string? Connections { get; set; }
    public int RelevanceScore { get; set; } = 100;
    public string? SurfaceConditions { get; set; }
    public DateTime? SurfaceAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Surfaced { get; set; } = "false";
    public DateTime? SurfacedAt { get; set; }
    public string Archived { get; set; } = "false";
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
