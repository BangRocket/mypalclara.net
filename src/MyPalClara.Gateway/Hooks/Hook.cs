using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Hooks;

public enum HookType { Shell, Code }

public class Hook
{
    public required string Name { get; init; }
    public required string Event { get; init; }
    public HookType Type { get; init; } = HookType.Shell;
    public string? Command { get; init; }
    public Func<GatewayEvent, Task>? Handler { get; init; }
    public double Timeout { get; init; } = 30.0;
    public string? WorkingDir { get; init; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; init; }
}
