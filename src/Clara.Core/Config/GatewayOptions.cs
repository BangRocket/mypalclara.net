namespace Clara.Core.Config;

public class GatewayOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 18789;
    public string? Secret { get; set; }
    public int MaxToolRounds { get; set; } = 10;
    public int MaxToolResultChars { get; set; } = 50000;
}
