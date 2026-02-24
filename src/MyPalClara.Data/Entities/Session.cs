namespace MyPalClara.Data.Entities;

public class Session
{
    public string Id { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ContextId { get; set; } = "default";
    public string? Title { get; set; }
    public string Archived { get; set; } = "false";
    public DateTime StartedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public string? PreviousSessionId { get; set; }
    public string? ContextSnapshot { get; set; }
    public string? SessionSummary { get; set; }

    public Project Project { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = [];
}
