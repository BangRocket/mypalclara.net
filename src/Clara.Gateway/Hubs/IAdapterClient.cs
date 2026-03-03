namespace Clara.Gateway.Hubs;

public interface IAdapterClient
{
    Task ReceiveTextDelta(string sessionKey, string text);
    Task ReceiveToolStatus(string sessionKey, string toolName, string status);
    Task ReceiveComplete(string sessionKey);
    Task ReceiveError(string sessionKey, string error);
}
