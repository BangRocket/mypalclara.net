namespace Clara.Core.Config;

public class HeartbeatOptions
{
    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 30;
    public string ChecklistPath { get; set; } = "workspace/heartbeat.md";
}
