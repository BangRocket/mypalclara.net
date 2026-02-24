namespace MyPalClara.Data.Entities;

public class PersonalityTrait
{
    public string Id { get; set; } = null!;
    public string AgentId { get; set; } = "mypalclara";
    public string Category { get; set; } = null!;
    public string TraitKey { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string Source { get; set; } = "self";
    public string? Reason { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<PersonalityTraitHistory> History { get; set; } = [];
}
