using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyPalClara.Gateway.Hooks;
using MyPalClara.Modules.Sdk;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MyPalClara.Gateway.Scheduling;

public class Scheduler : IScheduler, IHostedService, IDisposable
{
    private readonly List<ScheduledTask> _tasks = [];
    private readonly List<TaskResult> _results = [];
    private readonly IEventBus _eventBus;
    private readonly ILogger<Scheduler> _logger;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private const int MaxResults = 100;

    public Scheduler(IEventBus eventBus, ILogger<Scheduler> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    // IHostedService
    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        InitializeNextRuns();
        _loopTask = RunLoopAsync(_cts.Token);
        _logger.LogInformation("Scheduler started with {Count} tasks", _tasks.Count);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        if (_loopTask != null)
        {
            try { await _loopTask; }
            catch (OperationCanceledException) { }
        }
        _logger.LogInformation("Scheduler stopped");
    }

    // IScheduler
    public void AddTask(ScheduledTask task)
    {
        lock (_lock)
        {
            _tasks.Add(task);
            CalculateNextRun(task);
        }
    }

    public bool RemoveTask(string name)
    {
        lock (_lock) { return _tasks.RemoveAll(t => t.Name == name) > 0; }
    }

    public bool EnableTask(string name)
    {
        lock (_lock)
        {
            var task = _tasks.Find(t => t.Name == name);
            if (task == null) return false;
            task.Enabled = true;
            CalculateNextRun(task);
            return true;
        }
    }

    public bool DisableTask(string name)
    {
        lock (_lock)
        {
            var task = _tasks.Find(t => t.Name == name);
            if (task == null) return false;
            task.Enabled = false;
            return true;
        }
    }

    public async Task RunTaskNowAsync(string name, CancellationToken ct = default)
    {
        ScheduledTask? task;
        lock (_lock) { task = _tasks.Find(t => t.Name == name); }
        if (task == null) throw new ArgumentException($"Task '{name}' not found");
        await ExecuteTaskAsync(task, ct);
    }

    public IReadOnlyList<ScheduledTask> GetTasks()
    {
        lock (_lock) { return [.. _tasks]; }
    }

    public IReadOnlyList<TaskResult> GetResults(int limit = 100)
    {
        lock (_lock) { return _results.TakeLast(Math.Min(limit, _results.Count)).ToList(); }
    }

    // YAML loading
    public void LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            _logger.LogDebug("No scheduler file at {Path}", path);
            return;
        }

        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var config = deserializer.Deserialize<SchedulerConfig>(yaml);
        if (config?.Tasks == null) return;

        foreach (var entry in config.Tasks)
        {
            var taskType = entry.Type?.ToLowerInvariant() switch
            {
                "interval" => TaskType.Interval,
                "cron" => TaskType.Cron,
                "one_shot" or "oneshot" => TaskType.OneShot,
                _ => TaskType.Interval
            };

            AddTask(new ScheduledTask
            {
                Name = entry.Name,
                Type = taskType,
                Command = entry.Command,
                Timeout = entry.Timeout,
                Interval = entry.Interval > 0 ? TimeSpan.FromSeconds(entry.Interval) : null,
                Cron = entry.Cron,
                Delay = entry.Delay > 0 ? TimeSpan.FromSeconds(entry.Delay) : null,
                Enabled = entry.Enabled,
                WorkingDir = entry.WorkingDir
            });
        }

        _logger.LogInformation("Loaded {Count} scheduled tasks from {Path}", config.Tasks.Count, path);
    }

    // Internal
    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(100, ct);
                await CheckAndRunDueTasksAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler loop error");
            }
        }
    }

    private async Task CheckAndRunDueTasksAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        List<ScheduledTask> dueTasks;

        lock (_lock)
        {
            dueTasks = _tasks
                .Where(t => t.Enabled && t.NextRun.HasValue && t.NextRun.Value <= now)
                .ToList();
        }

        foreach (var task in dueTasks)
        {
            await ExecuteTaskAsync(task, ct);
        }
    }

    private async Task ExecuteTaskAsync(ScheduledTask task, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        bool success;
        string? output = null, error = null;

        try
        {
            if (task.Handler != null)
            {
                using var taskCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                taskCts.CancelAfter(TimeSpan.FromSeconds(task.Timeout));
                await task.Handler(taskCts.Token);
                success = true;
            }
            else if (task.Command != null)
            {
                var evt = GatewayEvent.Create(EventTypes.ScheduledTaskRun,
                    data: new Dictionary<string, object> { ["task_name"] = task.Name });
                var result = await ShellExecutor.ExecuteAsync(task.Command, evt, task.WorkingDir, task.Timeout, ct);
                success = result.Success;
                output = result.Output;
                error = result.Error;
            }
            else
            {
                success = false;
                error = "No handler or command configured";
            }
        }
        catch (Exception ex)
        {
            success = false;
            error = ex.Message;
        }

        sw.Stop();
        task.LastRun = DateTime.UtcNow;
        task.RunCount++;

        var taskResult = new TaskResult(task.Name, success, output, error, DateTime.UtcNow, sw.Elapsed);
        AddResult(taskResult);

        // Emit event
        var eventType = success ? EventTypes.ScheduledTaskRun : EventTypes.ScheduledTaskError;
        await _eventBus.PublishAsync(GatewayEvent.Create(eventType,
            data: new Dictionary<string, object>
            {
                ["task_name"] = task.Name,
                ["success"] = success,
                ["duration_ms"] = sw.ElapsedMilliseconds
            }));

        // Calculate next run (disable one-shot tasks)
        if (task.Type == TaskType.OneShot)
        {
            task.Enabled = false;
            task.NextRun = null;
        }
        else
        {
            CalculateNextRun(task);
        }

        if (!success)
            _logger.LogWarning("Scheduled task {Name} failed: {Error}", task.Name, error);
    }

    private void InitializeNextRuns()
    {
        lock (_lock)
        {
            foreach (var task in _tasks.Where(t => t.Enabled))
                CalculateNextRun(task);
        }
    }

    private static void CalculateNextRun(ScheduledTask task)
    {
        var now = DateTime.UtcNow;
        task.NextRun = task.Type switch
        {
            TaskType.Interval => task.LastRun?.Add(task.Interval ?? TimeSpan.FromMinutes(1))
                ?? now.Add(task.Interval ?? TimeSpan.FromMinutes(1)),
            TaskType.Cron when task.Cron != null => CronParser.Parse(task.Cron).GetNextOccurrence(now),
            TaskType.OneShot when task.RunAt.HasValue => task.RunAt,
            TaskType.OneShot when task.Delay.HasValue => now.Add(task.Delay.Value),
            _ => null
        };
    }

    private void AddResult(TaskResult result)
    {
        lock (_lock)
        {
            _results.Add(result);
            if (_results.Count > MaxResults)
                _results.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

// YAML deserialization models
internal class SchedulerConfig
{
    public List<SchedulerEntry> Tasks { get; set; } = [];
}

internal class SchedulerEntry
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public string? Command { get; set; }
    public double Timeout { get; set; } = 300.0;
    public double Interval { get; set; }
    public string? Cron { get; set; }
    public double Delay { get; set; }
    public bool Enabled { get; set; } = true;
    public string? WorkingDir { get; set; }
}
