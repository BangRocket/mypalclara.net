using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Events;

public class ClaraEventBus : IClaraEventBus
{
    private readonly ConcurrentDictionary<string, List<(int Priority, Func<ClaraEvent, Task> Handler)>> _handlers = new();
    private readonly LinkedList<ClaraEvent> _recentEvents = new();
    private readonly object _recentLock = new();
    private const int MaxRecentEvents = 100;
    private readonly ILogger<ClaraEventBus> _logger;

    public ClaraEventBus(ILogger<ClaraEventBus> logger)
    {
        _logger = logger;
    }

    public void Subscribe(string eventType, Func<ClaraEvent, Task> handler, int priority = 0)
    {
        var list = _handlers.GetOrAdd(eventType, _ => new List<(int, Func<ClaraEvent, Task>)>());
        lock (list)
        {
            list.Add((priority, handler));
            list.Sort((a, b) => b.Priority.CompareTo(a.Priority)); // Higher priority first
        }
    }

    public void Unsubscribe(string eventType, Func<ClaraEvent, Task> handler)
    {
        if (_handlers.TryGetValue(eventType, out var list))
        {
            lock (list)
            {
                list.RemoveAll(entry => entry.Handler == handler);
            }
        }
    }

    public async Task PublishAsync(ClaraEvent evt)
    {
        // Store in recent events
        lock (_recentLock)
        {
            _recentEvents.AddLast(evt);
            while (_recentEvents.Count > MaxRecentEvents)
                _recentEvents.RemoveFirst();
        }

        if (!_handlers.TryGetValue(evt.Type, out var list))
            return;

        // Snapshot the handlers to avoid locking during execution
        (int Priority, Func<ClaraEvent, Task> Handler)[] snapshot;
        lock (list)
        {
            snapshot = list.ToArray();
        }

        // Run all handlers concurrently, errors isolated per handler
        var tasks = snapshot.Select(entry => RunHandler(entry.Handler, evt));
        await Task.WhenAll(tasks);
    }

    public IReadOnlyList<ClaraEvent> GetRecentEvents(int limit = 100)
    {
        lock (_recentLock)
        {
            return _recentEvents
                .Reverse()
                .Take(Math.Min(limit, MaxRecentEvents))
                .ToList();
        }
    }

    private async Task RunHandler(Func<ClaraEvent, Task> handler, ClaraEvent evt)
    {
        try
        {
            await handler(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event handler failed for event type {EventType}", evt.Type);
        }
    }
}
