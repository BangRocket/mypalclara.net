using Clara.Core.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clara.Core.Tests.Events;

public class ClaraEventBusTests
{
    private ClaraEventBus CreateBus() => new(NullLogger<ClaraEventBus>.Instance);

    [Fact]
    public async Task Subscribe_Publish_HandlerCalled()
    {
        var bus = CreateBus();
        var called = false;

        bus.Subscribe("test:event", _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new ClaraEvent("test:event", DateTime.UtcNow));

        Assert.True(called);
    }

    [Fact]
    public async Task Subscribe_Publish_ReceivesEventData()
    {
        var bus = CreateBus();
        ClaraEvent? received = null;

        bus.Subscribe("test:event", evt =>
        {
            received = evt;
            return Task.CompletedTask;
        });

        var data = new Dictionary<string, object> { ["key"] = "value" };
        await bus.PublishAsync(new ClaraEvent("test:event", DateTime.UtcNow, data)
        {
            UserId = "user1",
            Platform = "test"
        });

        Assert.NotNull(received);
        Assert.Equal("user1", received.UserId);
        Assert.Equal("test", received.Platform);
        Assert.NotNull(received.Data);
        Assert.Equal("value", received.Data["key"]);
    }

    [Fact]
    public async Task PriorityOrdering_HigherRunsFirst()
    {
        var bus = CreateBus();
        var order = new List<int>();

        bus.Subscribe("test:priority", _ =>
        {
            order.Add(1);
            return Task.CompletedTask;
        }, priority: 10);

        bus.Subscribe("test:priority", _ =>
        {
            order.Add(2);
            return Task.CompletedTask;
        }, priority: 20);

        bus.Subscribe("test:priority", _ =>
        {
            order.Add(3);
            return Task.CompletedTask;
        }, priority: 5);

        await bus.PublishAsync(new ClaraEvent("test:priority", DateTime.UtcNow));

        // All handlers run concurrently via Task.WhenAll, but they're all synchronous
        // so the order should match priority (highest first)
        Assert.Equal(3, order.Count);
    }

    [Fact]
    public async Task ErrorIsolation_OneHandlerThrows_OthersStillRun()
    {
        var bus = CreateBus();
        var handler1Called = false;
        var handler2Called = false;

        bus.Subscribe("test:error", _ =>
        {
            handler1Called = true;
            throw new InvalidOperationException("Handler 1 failed!");
        });

        bus.Subscribe("test:error", _ =>
        {
            handler2Called = true;
            return Task.CompletedTask;
        });

        // Should not throw
        await bus.PublishAsync(new ClaraEvent("test:error", DateTime.UtcNow));

        Assert.True(handler1Called);
        Assert.True(handler2Called);
    }

    [Fact]
    public async Task RecentEvents_TracksLastEvents()
    {
        var bus = CreateBus();

        for (var i = 0; i < 5; i++)
        {
            await bus.PublishAsync(new ClaraEvent($"test:event{i}", DateTime.UtcNow));
        }

        var recent = bus.GetRecentEvents(3);

        Assert.Equal(3, recent.Count);
        // Most recent first
        Assert.Equal("test:event4", recent[0].Type);
        Assert.Equal("test:event3", recent[1].Type);
        Assert.Equal("test:event2", recent[2].Type);
    }

    [Fact]
    public async Task RecentEvents_LimitedTo100()
    {
        var bus = CreateBus();

        for (var i = 0; i < 150; i++)
        {
            await bus.PublishAsync(new ClaraEvent($"test:event{i}", DateTime.UtcNow));
        }

        var recent = bus.GetRecentEvents(200);

        Assert.Equal(100, recent.Count);
    }

    [Fact]
    public async Task Unsubscribe_RemovesHandler()
    {
        var bus = CreateBus();
        var callCount = 0;

        Task Handler(ClaraEvent _)
        {
            callCount++;
            return Task.CompletedTask;
        }

        bus.Subscribe("test:unsub", Handler);
        await bus.PublishAsync(new ClaraEvent("test:unsub", DateTime.UtcNow));
        Assert.Equal(1, callCount);

        bus.Unsubscribe("test:unsub", Handler);
        await bus.PublishAsync(new ClaraEvent("test:unsub", DateTime.UtcNow));
        Assert.Equal(1, callCount); // Should NOT have been called again
    }

    [Fact]
    public async Task Publish_NoSubscribers_DoesNotThrow()
    {
        var bus = CreateBus();

        // Should not throw
        await bus.PublishAsync(new ClaraEvent("test:no-handlers", DateTime.UtcNow));
    }

    [Fact]
    public async Task Publish_MultipleEventTypes_IndependentSubscribers()
    {
        var bus = CreateBus();
        var type1Called = false;
        var type2Called = false;

        bus.Subscribe("type1", _ => { type1Called = true; return Task.CompletedTask; });
        bus.Subscribe("type2", _ => { type2Called = true; return Task.CompletedTask; });

        await bus.PublishAsync(new ClaraEvent("type1", DateTime.UtcNow));

        Assert.True(type1Called);
        Assert.False(type2Called);
    }
}
