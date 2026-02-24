namespace MyPalClara.Modules.Sdk;

public enum TaskType { Interval, Cron, OneShot }

public class ScheduledTask
{
    public required string Name { get; init; }
    public required TaskType Type { get; init; }
    public string? Command { get; init; }
    public Func<CancellationToken, Task>? Handler { get; init; }
    public double Timeout { get; init; } = 300.0;
    public TimeSpan? Interval { get; init; }
    public string? Cron { get; init; }
    public TimeSpan? Delay { get; init; }
    public DateTime? RunAt { get; init; }
    public bool Enabled { get; set; } = true;
    public string? WorkingDir { get; init; }

    // State (managed by scheduler)
    public DateTime? LastRun { get; set; }
    public DateTime? NextRun { get; set; }
    public int RunCount { get; set; }
}
