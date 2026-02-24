using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Events;
using MyPalClara.Gateway.Scheduling;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Scheduling;

public class SchedulerTests : IDisposable
{
    private readonly EventBus _eventBus = new();
    private readonly Scheduler _scheduler;

    public SchedulerTests()
    {
        _scheduler = new Scheduler(_eventBus, NullLogger<Scheduler>.Instance);
    }

    public void Dispose()
    {
        _scheduler.Dispose();
    }

    [Fact]
    public void AddTask_GetTasks_ReturnsTask()
    {
        var task = new ScheduledTask
        {
            Name = "test-task",
            Type = TaskType.Interval,
            Interval = TimeSpan.FromMinutes(5)
        };

        _scheduler.AddTask(task);
        var tasks = _scheduler.GetTasks();

        Assert.Single(tasks);
        Assert.Equal("test-task", tasks[0].Name);
    }

    [Fact]
    public void RemoveTask_RemovesIt()
    {
        _scheduler.AddTask(new ScheduledTask
        {
            Name = "to-remove",
            Type = TaskType.Interval,
            Interval = TimeSpan.FromMinutes(1)
        });

        var removed = _scheduler.RemoveTask("to-remove");

        Assert.True(removed);
        Assert.Empty(_scheduler.GetTasks());
    }

    [Fact]
    public void RemoveTask_NonExistent_ReturnsFalse()
    {
        Assert.False(_scheduler.RemoveTask("does-not-exist"));
    }

    [Fact]
    public void EnableTask_DisableTask_Toggles()
    {
        _scheduler.AddTask(new ScheduledTask
        {
            Name = "toggle-task",
            Type = TaskType.Interval,
            Interval = TimeSpan.FromMinutes(1),
            Enabled = true
        });

        var disabled = _scheduler.DisableTask("toggle-task");
        Assert.True(disabled);
        Assert.False(_scheduler.GetTasks()[0].Enabled);

        var enabled = _scheduler.EnableTask("toggle-task");
        Assert.True(enabled);
        Assert.True(_scheduler.GetTasks()[0].Enabled);
    }

    [Fact]
    public void EnableTask_NonExistent_ReturnsFalse()
    {
        Assert.False(_scheduler.EnableTask("nope"));
    }

    [Fact]
    public void DisableTask_NonExistent_ReturnsFalse()
    {
        Assert.False(_scheduler.DisableTask("nope"));
    }

    [Fact]
    public void IntervalTask_CalculatesNextRun()
    {
        var task = new ScheduledTask
        {
            Name = "interval-task",
            Type = TaskType.Interval,
            Interval = TimeSpan.FromMinutes(5)
        };

        _scheduler.AddTask(task);
        var added = _scheduler.GetTasks()[0];

        Assert.NotNull(added.NextRun);
        // NextRun should be approximately 5 minutes from now
        var diff = added.NextRun!.Value - DateTime.UtcNow;
        Assert.InRange(diff.TotalMinutes, 4.5, 5.5);
    }

    [Fact]
    public void CronTask_CalculatesNextRun()
    {
        var task = new ScheduledTask
        {
            Name = "cron-task",
            Type = TaskType.Cron,
            Cron = "0 9 * * *" // Daily at 9 AM
        };

        _scheduler.AddTask(task);
        var added = _scheduler.GetTasks()[0];

        Assert.NotNull(added.NextRun);
        // NextRun should be at 9:00 AM on some day
        Assert.Equal(9, added.NextRun!.Value.Hour);
        Assert.Equal(0, added.NextRun!.Value.Minute);
    }

    [Fact]
    public void OneShotTask_WithDelay_CalculatesNextRun()
    {
        var task = new ScheduledTask
        {
            Name = "oneshot-task",
            Type = TaskType.OneShot,
            Delay = TimeSpan.FromSeconds(30)
        };

        _scheduler.AddTask(task);
        var added = _scheduler.GetTasks()[0];

        Assert.NotNull(added.NextRun);
        var diff = added.NextRun!.Value - DateTime.UtcNow;
        Assert.InRange(diff.TotalSeconds, 25, 35);
    }

    [Fact]
    public void OneShotTask_WithRunAt_CalculatesNextRun()
    {
        var runAt = DateTime.UtcNow.AddHours(1);
        var task = new ScheduledTask
        {
            Name = "oneshot-runat",
            Type = TaskType.OneShot,
            RunAt = runAt
        };

        _scheduler.AddTask(task);
        var added = _scheduler.GetTasks()[0];

        Assert.NotNull(added.NextRun);
        Assert.Equal(runAt, added.NextRun!.Value);
    }

    [Fact]
    public void LoadFromFile_ParsesYaml()
    {
        var yaml = """
            tasks:
              - name: heartbeat
                type: interval
                command: echo alive
                interval: 60
                timeout: 10
              - name: cleanup
                type: cron
                command: echo cleanup
                cron: "0 3 * * *"
              - name: startup-init
                type: one_shot
                command: echo init
                delay: 5
            """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, yaml);
            _scheduler.LoadFromFile(tempFile);

            var tasks = _scheduler.GetTasks();
            Assert.Equal(3, tasks.Count);

            var heartbeat = tasks.First(t => t.Name == "heartbeat");
            Assert.Equal(TaskType.Interval, heartbeat.Type);
            Assert.Equal(TimeSpan.FromSeconds(60), heartbeat.Interval);
            Assert.Equal("echo alive", heartbeat.Command);
            Assert.Equal(10.0, heartbeat.Timeout);

            var cleanup = tasks.First(t => t.Name == "cleanup");
            Assert.Equal(TaskType.Cron, cleanup.Type);
            Assert.Equal("0 3 * * *", cleanup.Cron);

            var init = tasks.First(t => t.Name == "startup-init");
            Assert.Equal(TaskType.OneShot, init.Type);
            Assert.Equal(TimeSpan.FromSeconds(5), init.Delay);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromFile_NonExistentFile_DoesNotThrow()
    {
        // Should just log and return without error
        _scheduler.LoadFromFile("/nonexistent/scheduler.yaml");
        Assert.Empty(_scheduler.GetTasks());
    }

    [Fact]
    public async Task RunTaskNowAsync_HandlerTask_Executes()
    {
        var executed = false;
        var task = new ScheduledTask
        {
            Name = "handler-task",
            Type = TaskType.Interval,
            Interval = TimeSpan.FromHours(1),
            Handler = _ =>
            {
                executed = true;
                return Task.CompletedTask;
            }
        };

        _scheduler.AddTask(task);
        await _scheduler.RunTaskNowAsync("handler-task");

        Assert.True(executed);

        var results = _scheduler.GetResults();
        Assert.Single(results);
        Assert.Equal("handler-task", results[0].TaskName);
        Assert.True(results[0].Success);
    }

    [Fact]
    public async Task RunTaskNowAsync_NonExistentTask_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _scheduler.RunTaskNowAsync("ghost-task"));
    }

    [Fact]
    public async Task RunTaskNowAsync_FailingHandler_RecordsFailure()
    {
        var task = new ScheduledTask
        {
            Name = "failing-task",
            Type = TaskType.Interval,
            Interval = TimeSpan.FromHours(1),
            Handler = _ => throw new InvalidOperationException("boom")
        };

        _scheduler.AddTask(task);
        await _scheduler.RunTaskNowAsync("failing-task");

        var results = _scheduler.GetResults();
        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Contains("boom", results[0].Error);
    }

    [Fact]
    public async Task GetResults_TracksMultipleExecutions()
    {
        var counter = 0;
        var task = new ScheduledTask
        {
            Name = "counter-task",
            Type = TaskType.Interval,
            Interval = TimeSpan.FromHours(1),
            Handler = _ =>
            {
                counter++;
                return Task.CompletedTask;
            }
        };

        _scheduler.AddTask(task);
        await _scheduler.RunTaskNowAsync("counter-task");
        await _scheduler.RunTaskNowAsync("counter-task");
        await _scheduler.RunTaskNowAsync("counter-task");

        Assert.Equal(3, counter);

        var results = _scheduler.GetResults();
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task GetResults_RespectsLimit()
    {
        var task = new ScheduledTask
        {
            Name = "limited-task",
            Type = TaskType.Interval,
            Interval = TimeSpan.FromHours(1),
            Handler = _ => Task.CompletedTask
        };

        _scheduler.AddTask(task);
        for (var i = 0; i < 5; i++)
            await _scheduler.RunTaskNowAsync("limited-task");

        var limited = _scheduler.GetResults(2);
        Assert.Equal(2, limited.Count);
    }

    [Fact]
    public async Task RunTaskNowAsync_IncrementsRunCount()
    {
        var task = new ScheduledTask
        {
            Name = "count-task",
            Type = TaskType.Interval,
            Interval = TimeSpan.FromHours(1),
            Handler = _ => Task.CompletedTask
        };

        _scheduler.AddTask(task);
        await _scheduler.RunTaskNowAsync("count-task");
        await _scheduler.RunTaskNowAsync("count-task");

        var updated = _scheduler.GetTasks().First(t => t.Name == "count-task");
        Assert.Equal(2, updated.RunCount);
        Assert.NotNull(updated.LastRun);
    }

    [Fact]
    public async Task RunTaskNowAsync_NoHandlerNoCommand_RecordsFailure()
    {
        var task = new ScheduledTask
        {
            Name = "empty-task",
            Type = TaskType.Interval,
            Interval = TimeSpan.FromHours(1)
        };

        _scheduler.AddTask(task);
        await _scheduler.RunTaskNowAsync("empty-task");

        var results = _scheduler.GetResults();
        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Contains("No handler or command configured", results[0].Error);
    }
}
