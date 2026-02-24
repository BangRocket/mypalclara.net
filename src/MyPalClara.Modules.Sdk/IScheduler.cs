namespace MyPalClara.Modules.Sdk;

public interface IScheduler
{
    void AddTask(ScheduledTask task);
    bool RemoveTask(string name);
    bool EnableTask(string name);
    bool DisableTask(string name);
    Task RunTaskNowAsync(string name, CancellationToken ct = default);
    IReadOnlyList<ScheduledTask> GetTasks();
    IReadOnlyList<TaskResult> GetResults(int limit = 100);
}

public record TaskResult(
    string TaskName,
    bool Success,
    string? Output,
    string? Error,
    DateTime ExecutedAt,
    TimeSpan Duration);
