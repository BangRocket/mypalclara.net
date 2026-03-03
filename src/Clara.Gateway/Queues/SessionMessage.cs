namespace Clara.Gateway.Queues;

public record SessionMessage(
    string SessionKey,
    string UserId,
    string Platform,
    string Content,
    string ConnectionId,
    DateTime ReceivedAt);
