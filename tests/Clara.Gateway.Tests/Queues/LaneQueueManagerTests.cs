using Clara.Gateway.Queues;

namespace Clara.Gateway.Tests.Queues;

public class LaneQueueManagerTests
{
    private static SessionMessage MakeMessage(string sessionKey, string content = "hello") =>
        new(sessionKey, "user1", "test", content, "conn1", DateTime.UtcNow);

    [Fact]
    public async Task Enqueue_and_read_from_same_lane()
    {
        var manager = new LaneQueueManager();
        var msg = MakeMessage("lane1");

        await manager.EnqueueAsync("lane1", msg);

        var reader = manager.GetReader("lane1");
        Assert.NotNull(reader);

        var read = await reader.ReadAsync();
        Assert.Equal("hello", read.Content);
        Assert.Equal("lane1", read.SessionKey);
    }

    [Fact]
    public async Task Different_lanes_are_independent()
    {
        var manager = new LaneQueueManager();

        await manager.EnqueueAsync("lane-a", MakeMessage("lane-a", "alpha"));
        await manager.EnqueueAsync("lane-b", MakeMessage("lane-b", "beta"));

        var readerA = manager.GetReader("lane-a");
        var readerB = manager.GetReader("lane-b");

        Assert.NotNull(readerA);
        Assert.NotNull(readerB);

        var msgA = await readerA.ReadAsync();
        var msgB = await readerB.ReadAsync();

        Assert.Equal("alpha", msgA.Content);
        Assert.Equal("beta", msgB.Content);
    }

    [Fact]
    public void GetReader_returns_null_for_unknown_lane()
    {
        var manager = new LaneQueueManager();
        Assert.Null(manager.GetReader("nonexistent"));
    }

    [Fact]
    public async Task GetActiveLanes_lists_lanes_with_messages()
    {
        var manager = new LaneQueueManager();

        await manager.EnqueueAsync("lane-x", MakeMessage("lane-x"));
        await manager.EnqueueAsync("lane-y", MakeMessage("lane-y"));

        var lanes = manager.GetActiveLanes();
        Assert.Contains("lane-x", lanes);
        Assert.Contains("lane-y", lanes);
        Assert.Equal(2, lanes.Count);
    }

    [Fact]
    public async Task RemoveLane_completes_the_channel()
    {
        var manager = new LaneQueueManager();
        await manager.EnqueueAsync("lane1", MakeMessage("lane1"));

        var reader = manager.GetReader("lane1");
        Assert.NotNull(reader);

        manager.RemoveLane("lane1");

        // After completing, reading remaining items should work, then Completion fires
        var msg = await reader.ReadAsync();
        Assert.Equal("hello", msg.Content);

        // No more items — channel is completed
        Assert.False(reader.TryRead(out _));
        Assert.True(reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task RemoveLane_makes_lane_unavailable()
    {
        var manager = new LaneQueueManager();
        await manager.EnqueueAsync("lane1", MakeMessage("lane1"));

        manager.RemoveLane("lane1");

        Assert.Null(manager.GetReader("lane1"));
        Assert.DoesNotContain("lane1", manager.GetActiveLanes());
    }

    [Fact]
    public async Task Enqueue_preserves_message_order()
    {
        var manager = new LaneQueueManager();

        await manager.EnqueueAsync("lane1", MakeMessage("lane1", "first"));
        await manager.EnqueueAsync("lane1", MakeMessage("lane1", "second"));
        await manager.EnqueueAsync("lane1", MakeMessage("lane1", "third"));

        var reader = manager.GetReader("lane1")!;

        Assert.Equal("first", (await reader.ReadAsync()).Content);
        Assert.Equal("second", (await reader.ReadAsync()).Content);
        Assert.Equal("third", (await reader.ReadAsync()).Content);
    }
}
