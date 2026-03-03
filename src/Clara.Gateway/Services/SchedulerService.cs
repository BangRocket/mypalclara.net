using System.Diagnostics;
using Clara.Core.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Clara.Gateway.Services;

public class SchedulerService : BackgroundService
{
    private readonly IClaraEventBus _eventBus;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SchedulerService> _logger;
    private readonly List<ScheduledTask> _tasks = [];

    public SchedulerService(
        IClaraEventBus eventBus,
        IConfiguration configuration,
        ILogger<SchedulerService> logger)
    {
        _eventBus = eventBus;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LoadTasks();

        if (_tasks.Count == 0)
        {
            _logger.LogInformation("No scheduled tasks configured");
            return;
        }

        _logger.LogInformation("Scheduler started with {Count} tasks", _tasks.Count);

        // Calculate initial NextRun for each task
        var now = DateTime.UtcNow;
        foreach (var task in _tasks)
            CalculateNextRun(task, now);

        while (!stoppingToken.IsCancellationRequested)
        {
            now = DateTime.UtcNow;

            foreach (var task in _tasks)
            {
                if (!task.Enabled) continue;
                if (task.NextRun is null || now < task.NextRun) continue;

                await ExecuteTaskAsync(task, stoppingToken);
                task.LastRun = now;
                task.HasRun = true;
                CalculateNextRun(task, now);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private void LoadTasks()
    {
        var yamlPath = _configuration.GetValue("Scheduler:Directory", "scheduler.yaml");
        if (yamlPath is null || !File.Exists(yamlPath))
        {
            _logger.LogDebug("No scheduler config found at {Path}", yamlPath);
            return;
        }

        try
        {
            var yaml = File.ReadAllText(yamlPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var config = deserializer.Deserialize<SchedulerConfig>(yaml);
            if (config?.Tasks != null)
                _tasks.AddRange(config.Tasks.Where(t => t.Enabled));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load scheduler config from {Path}", yamlPath);
        }
    }

    private static void CalculateNextRun(ScheduledTask task, DateTime now)
    {
        switch (task.Type)
        {
            case TaskType.Interval when task.IntervalSeconds.HasValue:
                task.NextRun = (task.LastRun ?? now).AddSeconds(task.IntervalSeconds.Value);
                break;

            case TaskType.Cron when task.CronExpression is not null:
                task.NextRun = CronParser.GetNextOccurrence(task.CronExpression, now);
                break;

            case TaskType.OneShot when !task.HasRun:
                if (task.RunAt.HasValue)
                    task.NextRun = task.RunAt.Value;
                else if (task.DelaySeconds.HasValue)
                    task.NextRun = now.AddSeconds(task.DelaySeconds.Value);
                break;

            case TaskType.OneShot when task.HasRun:
                task.NextRun = null; // Don't run again
                break;
        }
    }

    private async Task ExecuteTaskAsync(ScheduledTask task, CancellationToken ct)
    {
        _logger.LogInformation("Running scheduled task: {TaskName}", task.Name);

        try
        {
            await _eventBus.PublishAsync(new ClaraEvent(SchedulerEvents.TaskRun, DateTime.UtcNow,
                new Dictionary<string, object> { ["task"] = task.Name }));

            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                ArgumentList = { OperatingSystem.IsWindows() ? "/c" : "-c", task.Command },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                _logger.LogError("Failed to start process for task {TaskName}", task.Name);
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(task.TimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                _logger.LogWarning("Task {TaskName} timed out after {Timeout}s", task.Name, task.TimeoutSeconds);
            }

            if (process.ExitCode != 0)
                _logger.LogWarning("Task {TaskName} exited with code {ExitCode}", task.Name, process.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing task {TaskName}", task.Name);
            await _eventBus.PublishAsync(new ClaraEvent(SchedulerEvents.TaskError, DateTime.UtcNow,
                new Dictionary<string, object> { ["task"] = task.Name, ["error"] = ex.Message }));
        }
    }
}

internal class SchedulerConfig
{
    public List<ScheduledTask>? Tasks { get; set; }
}
