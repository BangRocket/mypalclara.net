using System.Collections.Concurrent;
using Cronos;
using MyPalClara.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Gateway.Scheduling;

/// <summary>
/// Background service that evaluates cron expressions every 60 seconds
/// and fires matching jobs. Tracks last-run per job to prevent double-fire.
/// </summary>
public sealed class CronService : BackgroundService
{
    private readonly ClaraConfig _config;
    private readonly IEnumerable<IScheduledJob> _jobs;
    private readonly ILogger<CronService> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastRun = new();

    public CronService(ClaraConfig config, IEnumerable<IScheduledJob> jobs, ILogger<CronService> logger)
    {
        _config = config;
        _jobs = jobs;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Scheduler.Enabled)
        {
            _logger.LogInformation("Scheduler is disabled");
            return;
        }

        _logger.LogInformation("CronService started with {Count} configured jobs", _config.Scheduler.Jobs.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                await EvaluateJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CronService tick failed");
            }
        }
    }

    private async Task EvaluateJobsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        foreach (var jobConfig in _config.Scheduler.Jobs)
        {
            if (!jobConfig.Enabled) continue;

            var job = _jobs.FirstOrDefault(j => j.Name.Equals(jobConfig.Name, StringComparison.OrdinalIgnoreCase));
            if (job is null)
            {
                _logger.LogDebug("No job implementation found for '{Name}'", jobConfig.Name);
                continue;
            }

            try
            {
                var cron = CronExpression.Parse(jobConfig.CronExpression);
                var lastRun = _lastRun.GetValueOrDefault(jobConfig.Name, DateTime.MinValue);
                var next = cron.GetNextOccurrence(lastRun, inclusive: false);

                if (next is not null && next <= now)
                {
                    _logger.LogInformation("Firing scheduled job '{Name}'", jobConfig.Name);
                    _lastRun[jobConfig.Name] = now;

                    try
                    {
                        await job.ExecuteAsync(ct);
                        _logger.LogInformation("Scheduled job '{Name}' completed", jobConfig.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Scheduled job '{Name}' failed", jobConfig.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse cron expression for '{Name}': {Expr}",
                    jobConfig.Name, jobConfig.CronExpression);
            }
        }
    }
}
