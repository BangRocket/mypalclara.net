namespace MyPalClara.Gateway.Hooks;

public record HookResult(
    string HookName,
    string EventType,
    bool Success,
    string? Output,
    string? Error,
    DateTime ExecutedAt,
    TimeSpan Duration);
