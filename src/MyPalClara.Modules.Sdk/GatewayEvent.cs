namespace MyPalClara.Modules.Sdk;

public record GatewayEvent(
    string Type,
    DateTime Timestamp,
    string? NodeId = null,
    string? Platform = null,
    string? UserId = null,
    string? ChannelId = null,
    string? RequestId = null,
    Dictionary<string, object>? Data = null)
{
    public static GatewayEvent Create(string type, string? userId = null, string? channelId = null,
        string? nodeId = null, string? platform = null, string? requestId = null,
        Dictionary<string, object>? data = null)
        => new(type, DateTime.UtcNow, nodeId, platform, userId, channelId, requestId, data);
}
