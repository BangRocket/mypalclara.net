using System.Collections.Concurrent;
using Clara.Gateway.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Clara.Gateway.Pipeline.Middleware;

public class RateLimitMiddleware : IPipelineMiddleware
{
    private readonly IHubContext<AdapterHub, IAdapterClient> _hubContext;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly ConcurrentDictionary<string, UserRateState> _userStates = new();
    private readonly int _maxPerMinute;

    public RateLimitMiddleware(
        IHubContext<AdapterHub, IAdapterClient> hubContext,
        ILogger<RateLimitMiddleware> logger,
        int maxPerMinute = 30)
    {
        _hubContext = hubContext;
        _logger = logger;
        _maxPerMinute = maxPerMinute;
    }

    public int Order => 10;

    public async Task InvokeAsync(PipelineContext context, CancellationToken ct = default)
    {
        var state = _userStates.GetOrAdd(context.UserId, _ => new UserRateState());

        state.Prune();

        if (state.Count >= _maxPerMinute)
        {
            _logger.LogWarning("Rate limit exceeded for user {UserId} ({Count}/{Max} per minute)",
                context.UserId, state.Count, _maxPerMinute);

            await _hubContext.Clients.Group(context.SessionKey)
                .ReceiveError(context.SessionKey, "Rate limit exceeded. Please wait a moment before sending more messages.");

            context.Cancelled = true;
            return;
        }

        state.Record();
    }

    private class UserRateState
    {
        private readonly List<DateTime> _timestamps = [];
        private readonly object _lock = new();

        public int Count
        {
            get
            {
                lock (_lock) return _timestamps.Count;
            }
        }

        public void Record()
        {
            lock (_lock) _timestamps.Add(DateTime.UtcNow);
        }

        public void Prune()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            lock (_lock) _timestamps.RemoveAll(t => t < cutoff);
        }
    }
}
