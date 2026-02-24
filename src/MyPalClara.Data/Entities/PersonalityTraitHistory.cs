namespace MyPalClara.Data.Entities;

public class PersonalityTraitHistory
{
    public string Id { get; set; } = null!;
    public string TraitId { get; set; } = null!;
    public string AgentId { get; set; } = null!;
    public string Event { get; set; } = null!;
    public string? OldContent { get; set; }
    public string? NewContent { get; set; }
    public string? OldCategory { get; set; }
    public string? NewCategory { get; set; }
    public string? Reason { get; set; }
    public string Source { get; set; } = "self";
    public string? TriggerContext { get; set; }
    public DateTime CreatedAt { get; set; }

    public PersonalityTrait Trait { get; set; } = null!;
}
