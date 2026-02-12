namespace MyPalClara.Core.Configuration;

public sealed class GatewaySettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 18789;
    public string Url { get; set; } = "ws://127.0.0.1:18789";
    public string Secret { get; set; } = "";
    public int IoThreads { get; set; } = 20;
    public int LlmThreads { get; set; } = 10;
    public int MaxToolIterations { get; set; } = 75;
    public int MaxToolResultChars { get; set; } = 50000;
    public bool AutoContinueEnabled { get; set; } = true;
    public int AutoContinueMax { get; set; } = 1;
    public string PluginsDir { get; set; } = "plugins";
}
