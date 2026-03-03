namespace Clara.Core.Events;

public record ClaraEvent(string Type, DateTime Timestamp, Dictionary<string, object>? Data = null)
{
    public string? UserId { get; init; }
    public string? SessionKey { get; init; }
    public string? Platform { get; init; }
}
