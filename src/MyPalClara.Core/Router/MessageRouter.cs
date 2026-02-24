using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Core.Router;

/// <summary>
/// Per-channel message queuing with debounce and deduplication.
/// Thread-safe via SemaphoreSlim for coordinated state mutations.
/// </summary>
public class MessageRouter
{
    private readonly ConcurrentDictionary<string, ActiveRequest> _active = new();
    private readonly ConcurrentDictionary<string, List<QueuedRequest>> _queues = new();
    private readonly Dictionary<string, RequestStatus> _requests = new();
    private readonly Dictionary<string, DateTime> _seenMessages = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceCts = new();
    private readonly ConcurrentDictionary<string, List<QueuedRequest>> _debounceRequests = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<MessageRouter> _logger;

    private readonly double _debounceSeconds;
    private readonly int _dedupWindowSeconds;
    private readonly int _dedupMaxEntries;

    /// <summary>
    /// Fired when a request is ready for processing (either immediately acquired or after debounce/dequeue).
    /// </summary>
    public event Func<QueuedRequest, Task>? OnRequestReady;

    public MessageRouter(ILogger<MessageRouter> logger)
    {
        _logger = logger;
        _debounceSeconds = double.TryParse(
            Environment.GetEnvironmentVariable("MESSAGE_DEBOUNCE_SECONDS"), out var d) ? d : 2.0;
        _dedupWindowSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("DEDUP_WINDOW_SECONDS"), out var w) ? w : 30;
        _dedupMaxEntries = int.TryParse(
            Environment.GetEnvironmentVariable("DEDUP_MAX_ENTRIES"), out var m) ? m : 1000;
    }

    /// <summary>
    /// Submit a request for processing.
    /// Returns (acquired, position) where acquired=true means process immediately,
    /// position=-1 means rejected as duplicate.
    /// </summary>
    public async Task<(bool Acquired, int Position)> SubmitAsync(
        QueuedRequest request, bool skipDedup = false, bool isMention = false)
    {
        await _lock.WaitAsync();
        try
        {
            // 1. Check for duplicate
            if (!skipDedup && IsDuplicate(request.UserId, request.ChannelId, request.Content))
            {
                _logger.LogDebug("Duplicate message rejected: {RequestId}", request.RequestId);
                _requests[request.RequestId] = RequestStatus.Failed;
                return (false, -1);
            }

            _requests[request.RequestId] = RequestStatus.Pending;

            // 2. If channel is currently debouncing, add to debounce list and reset timer
            if (_debounceCts.ContainsKey(request.ChannelId))
            {
                _requests[request.RequestId] = RequestStatus.Debounce;
                var debounceList = _debounceRequests.GetOrAdd(request.ChannelId, _ => []);
                debounceList.Add(request);

                // Cancel existing debounce timer to reset it
                if (_debounceCts.TryGetValue(request.ChannelId, out var existingCts))
                {
                    await existingCts.CancelAsync();
                }

                // Start new debounce timer
                StartDebounceTimer(request.ChannelId);

                _logger.LogDebug(
                    "Added to debounce for channel {ChannelId}, {Count} pending",
                    request.ChannelId,
                    debounceList.Count);
                return (false, 0);
            }

            // 3. If channel is free (no active request)
            if (!_active.ContainsKey(request.ChannelId))
            {
                // For server channels (not DM, not mention): start debounce
                if (!isMention && !IsDmChannel(request))
                {
                    _requests[request.RequestId] = RequestStatus.Debounce;
                    var debounceList = _debounceRequests.GetOrAdd(request.ChannelId, _ => []);
                    debounceList.Add(request);
                    StartDebounceTimer(request.ChannelId);

                    _logger.LogDebug(
                        "Starting debounce for channel {ChannelId}",
                        request.ChannelId);
                    return (false, 0);
                }

                // DM or mention: acquire immediately
                return AcquireChannel(request);
            }

            // 4. Channel has active request: enqueue
            _requests[request.RequestId] = RequestStatus.Queued;
            var queue = _queues.GetOrAdd(request.ChannelId, _ => []);
            queue.Add(request);
            request.Position = queue.Count;

            _logger.LogDebug(
                "Queued request {RequestId} for channel {ChannelId} at position {Position}",
                request.RequestId, request.ChannelId, request.Position);

            return (false, request.Position);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Called when processing completes. Returns the next queued request if any.
    /// </summary>
    public async Task<QueuedRequest?> CompleteAsync(string requestId)
    {
        await _lock.WaitAsync();
        try
        {
            _requests[requestId] = RequestStatus.Completed;

            // Find and remove from active
            var channelId = _active
                .Where(kv => kv.Value.Request.RequestId == requestId)
                .Select(kv => kv.Key)
                .FirstOrDefault();

            if (channelId is null)
            {
                _logger.LogWarning("CompleteAsync called for unknown request {RequestId}", requestId);
                return null;
            }

            _active.TryRemove(channelId, out _);

            // Check queue for next request
            if (_queues.TryGetValue(channelId, out var queue) && queue.Count > 0)
            {
                var next = queue[0];
                queue.RemoveAt(0);

                // Update positions for remaining items
                for (var i = 0; i < queue.Count; i++)
                    queue[i].Position = i + 1;

                if (queue.Count == 0)
                    _queues.TryRemove(channelId, out _);

                // Acquire channel for next request
                var (acquired, _) = AcquireChannel(next);
                if (acquired)
                {
                    _logger.LogDebug(
                        "Dequeued next request {RequestId} for channel {ChannelId}",
                        next.RequestId, channelId);

                    // Fire event outside lock
                    _ = Task.Run(async () =>
                    {
                        if (OnRequestReady is not null)
                            await OnRequestReady(next);
                    });

                    return next;
                }
            }

            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Cancel a request. Returns true if found and cancelled.
    /// </summary>
    public async Task<bool> CancelAsync(string requestId)
    {
        await _lock.WaitAsync();
        try
        {
            // Check if it's active
            var activeEntry = _active
                .Where(kv => kv.Value.Request.RequestId == requestId)
                .Select(kv => (Key: kv.Key, Value: kv.Value))
                .FirstOrDefault();

            if (activeEntry.Value is not null)
            {
                _requests[requestId] = RequestStatus.Cancelled;
                await activeEntry.Value.Cts.CancelAsync();
                _active.TryRemove(activeEntry.Key, out _);
                _logger.LogInformation("Cancelled active request {RequestId}", requestId);
                return true;
            }

            // Check if it's queued
            foreach (var (channelId, queue) in _queues)
            {
                var idx = queue.FindIndex(r => r.RequestId == requestId);
                if (idx >= 0)
                {
                    queue.RemoveAt(idx);
                    _requests[requestId] = RequestStatus.Cancelled;

                    // Update positions
                    for (var i = idx; i < queue.Count; i++)
                        queue[i].Position = i + 1;

                    if (queue.Count == 0)
                        _queues.TryRemove(channelId, out _);

                    _logger.LogInformation("Cancelled queued request {RequestId}", requestId);
                    return true;
                }
            }

            // Check debounce lists
            foreach (var (channelId, debounceList) in _debounceRequests)
            {
                var idx = debounceList.FindIndex(r => r.RequestId == requestId);
                if (idx >= 0)
                {
                    debounceList.RemoveAt(idx);
                    _requests[requestId] = RequestStatus.Cancelled;
                    _logger.LogInformation("Cancelled debouncing request {RequestId}", requestId);
                    return true;
                }
            }

            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Get current router statistics.
    /// </summary>
    public async Task<RouterStats> GetStatsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var totalQueued = _queues.Values.Sum(q => q.Count);
            var byStatus = _requests
                .GroupBy(kv => kv.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            return new RouterStats(
                ActiveChannels: _active.Count,
                TotalQueued: totalQueued,
                DebouncingChannels: _debounceCts.Count,
                ByStatus: byStatus);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Acquire the channel for immediate processing.
    /// Must be called while holding _lock.
    /// </summary>
    private (bool Acquired, int Position) AcquireChannel(QueuedRequest request)
    {
        var active = new ActiveRequest { Request = request };
        _active[request.ChannelId] = active;
        _requests[request.RequestId] = RequestStatus.Active;
        request.Position = 0;

        _logger.LogDebug(
            "Channel {ChannelId} acquired by request {RequestId}",
            request.ChannelId, request.RequestId);

        return (true, 0);
    }

    /// <summary>
    /// Start (or restart) the debounce timer for a channel.
    /// Must be called while holding _lock.
    /// </summary>
    private void StartDebounceTimer(string channelId)
    {
        var cts = new CancellationTokenSource();
        _debounceCts[channelId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_debounceSeconds), cts.Token);

                // Timer expired: consolidate and fire
                await OnDebounceExpiredAsync(channelId);
            }
            catch (OperationCanceledException)
            {
                // Timer was reset by a new message -- this is expected
            }
        });
    }

    /// <summary>
    /// Called when debounce timer expires. Consolidates pending requests and acquires channel.
    /// </summary>
    private async Task OnDebounceExpiredAsync(string channelId)
    {
        QueuedRequest? consolidated = null;

        await _lock.WaitAsync();
        try
        {
            // Remove debounce state
            _debounceCts.TryRemove(channelId, out _);

            if (!_debounceRequests.TryRemove(channelId, out var pending) || pending.Count == 0)
                return;

            // Consolidate: use the last request as the base, join all content
            consolidated = pending[^1];

            if (pending.Count > 1)
            {
                var joinedContent = string.Join("\n", pending.Select(r => r.Content));
                // Create a new QueuedRequest with consolidated content
                consolidated = new QueuedRequest
                {
                    RequestId = consolidated.RequestId,
                    ChannelId = consolidated.ChannelId,
                    UserId = consolidated.UserId,
                    Content = joinedContent,
                    WebSocket = consolidated.WebSocket,
                    NodeId = consolidated.NodeId,
                    QueuedAt = pending[0].QueuedAt,
                    IsBatchable = true,
                    RawRequest = consolidated.RawRequest
                };

                // Mark earlier requests as completed (absorbed into consolidated)
                for (var i = 0; i < pending.Count - 1; i++)
                    _requests[pending[i].RequestId] = RequestStatus.Completed;
            }

            // If channel is now active (race condition), queue instead
            if (_active.ContainsKey(channelId))
            {
                _requests[consolidated.RequestId] = RequestStatus.Queued;
                var queue = _queues.GetOrAdd(channelId, _ => []);
                queue.Add(consolidated);
                consolidated.Position = queue.Count;

                _logger.LogDebug(
                    "Debounce expired but channel {ChannelId} is active; queued {RequestId}",
                    channelId, consolidated.RequestId);
                consolidated = null; // Already queued; don't fire event
                return;
            }

            // Acquire channel
            AcquireChannel(consolidated);

            _logger.LogDebug(
                "Debounce expired for channel {ChannelId}, consolidated {Count} messages into {RequestId}",
                channelId, pending.Count, consolidated.RequestId);
        }
        finally
        {
            _lock.Release();
        }

        // Fire event outside lock
        if (consolidated is not null && OnRequestReady is not null)
        {
            try
            {
                await OnRequestReady(consolidated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnRequestReady handler failed for {RequestId}", consolidated.RequestId);
            }
        }
    }

    /// <summary>
    /// Check if a message is a duplicate based on SHA256 fingerprint within the dedup window.
    /// Must be called while holding _lock.
    /// </summary>
    private bool IsDuplicate(string userId, string channelId, string content)
    {
        // Clean up expired entries if over max
        if (_seenMessages.Count > _dedupMaxEntries)
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-_dedupWindowSeconds);
            var expired = _seenMessages
                .Where(kv => kv.Value < cutoff)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in expired)
                _seenMessages.Remove(key);
        }

        var fingerprint = ComputeFingerprint(userId, channelId, content);
        var now = DateTime.UtcNow;

        if (_seenMessages.TryGetValue(fingerprint, out var seenAt))
        {
            if ((now - seenAt).TotalSeconds < _dedupWindowSeconds)
                return true;
        }

        _seenMessages[fingerprint] = now;
        return false;
    }

    /// <summary>
    /// Determine if a request represents a DM channel.
    /// Heuristic: channel IDs starting with "dm-" are DMs.
    /// </summary>
    private static bool IsDmChannel(QueuedRequest request)
    {
        return request.ChannelId.StartsWith("dm-", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeFingerprint(string userId, string channelId, string content)
    {
        var raw = $"{userId}|{channelId}|{content}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash);
    }
}
