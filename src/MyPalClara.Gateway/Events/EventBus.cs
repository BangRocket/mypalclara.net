using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Events;

public class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<string, List<(Func<GatewayEvent, Task> Handler, int Priority)>> _subscribers = new();
    private readonly List<GatewayEvent> _history = [];
    private readonly object _historyLock = new();
    private readonly ILogger<EventBus>? _logger;
    private const int MaxHistory = 100;

    public EventBus(ILogger<EventBus>? logger = null)
    {
        _logger = logger;
    }

    public void Subscribe(string eventType, Func<GatewayEvent, Task> handler, int priority = 0)
    {
        var list = _subscribers.GetOrAdd(eventType, _ => []);
        lock (list)
        {
            list.Add((handler, priority));
            list.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
    }

    public void Unsubscribe(string eventType, Func<GatewayEvent, Task> handler)
    {
        if (_subscribers.TryGetValue(eventType, out var list))
        {
            lock (list)
            {
                list.RemoveAll(s => s.Handler == handler);
            }
        }
    }

    public async Task PublishAsync(GatewayEvent evt)
    {
        lock (_historyLock)
        {
            _history.Add(evt);
            if (_history.Count > MaxHistory)
                _history.RemoveAt(0);
        }

        if (!_subscribers.TryGetValue(evt.Type, out var list))
            return;

        List<(Func<GatewayEvent, Task> Handler, int Priority)> snapshot;
        lock (list)
        {
            snapshot = [.. list];
        }

        // Run handlers sequentially in priority order (higher first)
        // Each handler is isolated — errors don't block others
        foreach (var (handler, _) in snapshot)
        {
            try
            {
                await handler(evt);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Event handler for {EventType} failed", evt.Type);
            }
        }
    }

    public IReadOnlyList<GatewayEvent> GetRecentEvents(int limit = 100)
    {
        lock (_historyLock)
        {
            var count = Math.Min(limit, _history.Count);
            return _history.GetRange(_history.Count - count, count).AsReadOnly();
        }
    }
}
