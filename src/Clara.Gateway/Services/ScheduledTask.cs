namespace Clara.Gateway.Services;

public enum TaskType { Interval, Cron, OneShot }

public class ScheduledTask
{
    public string Name { get; set; } = "";
    public TaskType Type { get; set; }
    public int? IntervalSeconds { get; set; }
    public string? CronExpression { get; set; }
    public int? DelaySeconds { get; set; }
    public DateTime? RunAt { get; set; }
    public string Command { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 300;
    public bool Enabled { get; set; } = true;

    // Internal state
    public DateTime? LastRun { get; set; }
    public DateTime? NextRun { get; set; }
    public bool HasRun { get; set; }
}
