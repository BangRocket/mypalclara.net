namespace MyPalClara.Modules.Sdk;

public interface IEventBus
{
    void Subscribe(string eventType, Func<GatewayEvent, Task> handler, int priority = 0);
    void Unsubscribe(string eventType, Func<GatewayEvent, Task> handler);
    Task PublishAsync(GatewayEvent evt);
    IReadOnlyList<GatewayEvent> GetRecentEvents(int limit = 100);
}
