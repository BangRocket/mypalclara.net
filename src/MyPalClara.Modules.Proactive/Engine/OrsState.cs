namespace MyPalClara.Modules.Proactive.Engine;

public enum OrsState
{
    Wait,
    Think,
    Speak
}

public record OrsDecision(OrsState NextState, string? Reasoning = null, string? NoteContent = null, string? MessageContent = null)
{
    public static OrsDecision Parse(string stateStr)
    {
        return stateStr.Trim().ToUpperInvariant() switch
        {
            "WAIT" => new OrsDecision(OrsState.Wait),
            "THINK" => new OrsDecision(OrsState.Think),
            "SPEAK" => new OrsDecision(OrsState.Speak),
            _ => new OrsDecision(OrsState.Wait)
        };
    }
}
