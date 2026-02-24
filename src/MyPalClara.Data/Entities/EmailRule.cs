namespace MyPalClara.Data.Entities;

public class EmailRule
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? AccountId { get; set; }
    public string Name { get; set; } = null!;
    public string Enabled { get; set; } = "true";
    public int Priority { get; set; }
    public string RuleDefinition { get; set; } = null!;
    public string Importance { get; set; } = "normal";
    public string? CustomAlertMessage { get; set; }
    public string? OverridePing { get; set; }
    public string? PresetName { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public EmailAccount? Account { get; set; }
    public ICollection<EmailAlert> EmailAlerts { get; set; } = [];
}
