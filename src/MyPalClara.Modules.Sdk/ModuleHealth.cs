namespace MyPalClara.Modules.Sdk;

public record ModuleHealth(
    string Status,
    string? LastError = null,
    DateTime? LastActivity = null,
    Dictionary<string, object>? Metrics = null)
{
    public static ModuleHealth Running(DateTime? lastActivity = null, Dictionary<string, object>? metrics = null)
        => new("running", LastActivity: lastActivity ?? DateTime.UtcNow, Metrics: metrics);
    public static ModuleHealth Stopped() => new("stopped");
    public static ModuleHealth Failed(string error) => new("failed", LastError: error);
    public static ModuleHealth Disabled() => new("disabled");
}
