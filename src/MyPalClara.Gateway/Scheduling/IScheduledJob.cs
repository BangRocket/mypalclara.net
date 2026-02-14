namespace MyPalClara.Gateway.Scheduling;

/// <summary>A job that can be executed on a cron schedule.</summary>
public interface IScheduledJob
{
    string Name { get; }
    Task ExecuteAsync(CancellationToken ct);
}
