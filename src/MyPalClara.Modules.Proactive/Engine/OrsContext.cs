namespace MyPalClara.Modules.Proactive.Engine;

public class OrsContext
{
    public required string UserId { get; init; }
    public OrsState CurrentState { get; set; } = OrsState.Wait;
    public string? TemporalSummary { get; set; }
    public string? ConversationSummary { get; set; }
    public string? CrossChannelSummary { get; set; }
    public string? CadenceSummary { get; set; }
    public string? CalendarSummary { get; set; }
    public List<string> ActiveNotes { get; set; } = [];
    public DateTime? LastSpokeAt { get; set; }
    public DateTime? LastUserActivityAt { get; set; }
}
