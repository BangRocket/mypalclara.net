namespace Clara.Gateway.Hooks;

public class HookDefinition
{
    public string Name { get; set; } = "";
    public string Event { get; set; } = "";
    public string Command { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public string? WorkingDir { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
}
