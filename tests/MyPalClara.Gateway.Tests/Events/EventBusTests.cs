namespace MyPalClara.Gateway.Tests.Events;

using MyPalClara.Gateway.Events;
using MyPalClara.Modules.Sdk;

public class EventBusTests
{
    [Fact]
    public async Task Publish_CallsSubscribedHandler()
    {
        var bus = new EventBus();
        var called = false;
        bus.Subscribe("test:event", async evt => { called = true; });

        await bus.PublishAsync(new GatewayEvent("test:event", DateTime.UtcNow));

        Assert.True(called);
    }

    [Fact]
    public async Task Publish_HigherPriorityRunsFirst()
    {
        var bus = new EventBus();
        var order = new List<int>();
        bus.Subscribe("test:event", async evt => { order.Add(1); }, priority: 1);
        bus.Subscribe("test:event", async evt => { order.Add(10); }, priority: 10);

        await bus.PublishAsync(new GatewayEvent("test:event", DateTime.UtcNow));

        Assert.Equal(10, order[0]);
        Assert.Equal(1, order[1]);
    }

    [Fact]
    public async Task Publish_HandlerErrorDoesNotBlockOthers()
    {
        var bus = new EventBus();
        var secondCalled = false;
        bus.Subscribe("test:event", async evt => { throw new Exception("boom"); }, priority: 10);
        bus.Subscribe("test:event", async evt => { secondCalled = true; }, priority: 1);

        await bus.PublishAsync(new GatewayEvent("test:event", DateTime.UtcNow));

        Assert.True(secondCalled);
    }

    [Fact]
    public async Task Publish_DoesNotCallUnrelatedSubscribers()
    {
        var bus = new EventBus();
        var called = false;
        bus.Subscribe("other:event", async evt => { called = true; });

        await bus.PublishAsync(new GatewayEvent("test:event", DateTime.UtcNow));

        Assert.False(called);
    }

    [Fact]
    public async Task GetRecentEvents_TracksHistory()
    {
        var bus = new EventBus();
        await bus.PublishAsync(new GatewayEvent("test:one", DateTime.UtcNow));
        await bus.PublishAsync(new GatewayEvent("test:two", DateTime.UtcNow));

        var recent = bus.GetRecentEvents(10);

        Assert.Equal(2, recent.Count);
        Assert.Equal("test:one", recent[0].Type);
        Assert.Equal("test:two", recent[1].Type);
    }

    [Fact]
    public async Task Unsubscribe_RemovesHandler()
    {
        var bus = new EventBus();
        var count = 0;
        Task handler(GatewayEvent evt) { count++; return Task.CompletedTask; }
        bus.Subscribe("test:event", handler);

        await bus.PublishAsync(new GatewayEvent("test:event", DateTime.UtcNow));
        Assert.Equal(1, count);

        bus.Unsubscribe("test:event", handler);
        await bus.PublishAsync(new GatewayEvent("test:event", DateTime.UtcNow));
        Assert.Equal(1, count); // unchanged
    }
}
