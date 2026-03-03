using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Clara.Gateway.Queues;

public class LaneQueueManager
{
    private readonly ConcurrentDictionary<string, Channel<SessionMessage>> _lanes = new();

    public async Task EnqueueAsync(string sessionKey, SessionMessage message, CancellationToken ct = default)
    {
        var lane = _lanes.GetOrAdd(sessionKey, _ =>
            Channel.CreateBounded<SessionMessage>(new BoundedChannelOptions(50)
            {
                FullMode = BoundedChannelFullMode.Wait
            }));
        await lane.Writer.WriteAsync(message, ct);
    }

    public ChannelReader<SessionMessage>? GetReader(string sessionKey)
    {
        return _lanes.TryGetValue(sessionKey, out var lane) ? lane.Reader : null;
    }

    public IReadOnlyList<string> GetActiveLanes() => _lanes.Keys.ToList();

    public void RemoveLane(string sessionKey)
    {
        if (_lanes.TryRemove(sessionKey, out var lane))
            lane.Writer.Complete();
    }
}
