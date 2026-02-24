namespace MyPalClara.Data.Entities;

public class UserInteractionPattern
{
    public string UserId { get; set; } = null!;
    public DateTime? LastInteractionAt { get; set; }
    public string? LastInteractionChannel { get; set; }
    public string? LastInteractionSummary { get; set; }
    public string? LastInteractionEnergy { get; set; }
    public string? TypicalActiveHours { get; set; }
    public string? Timezone { get; set; }
    public string? TimezoneSource { get; set; }
    public int? AvgResponseTimeSeconds { get; set; }
    public string? ExplicitSignals { get; set; }
    public int? ProactiveSuccessRate { get; set; }
    public int? ProactiveResponseRate { get; set; }
    public string? PreferredProactiveTimes { get; set; }
    public string? PreferredProactiveTypes { get; set; }
    public string? TopicReceptiveness { get; set; }
    public string? ExplicitBoundaries { get; set; }
    public string? OpenThreads { get; set; }
    public double? ContactCadenceDays { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
