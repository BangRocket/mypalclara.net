namespace MyPalClara.Data.Entities;

public class ProactiveAssessment
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ContextSnapshot { get; set; }
    public string? Assessment { get; set; }
    public string Decision { get; set; } = null!;
    public string? Reasoning { get; set; }
    public string? NoteCreated { get; set; }
    public string? MessageSent { get; set; }
    public DateTime? NextCheckAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
