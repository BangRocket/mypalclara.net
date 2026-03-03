namespace Clara.Core.Events;

public interface IClaraEventBus
{
    void Subscribe(string eventType, Func<ClaraEvent, Task> handler, int priority = 0);
    void Unsubscribe(string eventType, Func<ClaraEvent, Task> handler);
    Task PublishAsync(ClaraEvent evt);
    IReadOnlyList<ClaraEvent> GetRecentEvents(int limit = 100);
}
