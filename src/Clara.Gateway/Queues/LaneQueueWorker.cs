using System.Collections.Concurrent;
using Clara.Gateway.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clara.Gateway.Queues;

public class LaneQueueWorker : BackgroundService
{
    private readonly LaneQueueManager _queueManager;
    private readonly QueueMetrics _metrics;
    private readonly IServiceProvider _services;
    private readonly ILogger<LaneQueueWorker> _logger;
    private readonly ConcurrentDictionary<string, Task> _activeLanes = new();

    public LaneQueueWorker(
        LaneQueueManager queueManager,
        QueueMetrics metrics,
        IServiceProvider services,
        ILogger<LaneQueueWorker> logger)
    {
        _queueManager = queueManager;
        _metrics = metrics;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Lane queue worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Check for new lanes that need processing tasks
            foreach (var laneKey in _queueManager.GetActiveLanes())
            {
                _activeLanes.TryAdd(laneKey, ProcessLaneAsync(laneKey, stoppingToken));
            }

            // Clean up completed lane tasks
            foreach (var (key, task) in _activeLanes)
            {
                if (task.IsCompleted)
                    _activeLanes.TryRemove(key, out _);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
        }

        // Wait for active lanes to finish
        if (!_activeLanes.IsEmpty)
        {
            _logger.LogInformation("Waiting for {Count} active lanes to complete", _activeLanes.Count);
            await Task.WhenAll(_activeLanes.Values);
        }
    }

    private async Task ProcessLaneAsync(string laneKey, CancellationToken ct)
    {
        var reader = _queueManager.GetReader(laneKey);
        if (reader is null) return;

        _logger.LogDebug("Processing lane {LaneKey}", laneKey);

        try
        {
            await foreach (var message in reader.ReadAllAsync(ct))
            {
                try
                {
                    await ProcessMessageAsync(message, ct);
                    _metrics.RecordProcessed();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message in lane {LaneKey}", laneKey);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lane {LaneKey} processing failed", laneKey);
        }

        _logger.LogDebug("Lane {LaneKey} processing completed", laneKey);
    }

    private async Task ProcessMessageAsync(SessionMessage message, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IMessagePipeline>();

        var context = new PipelineContext
        {
            SessionKey = message.SessionKey,
            UserId = message.UserId,
            Platform = message.Platform,
            Content = message.Content,
            ConnectionId = message.ConnectionId,
        };

        await pipeline.ProcessAsync(context, ct);
    }
}
