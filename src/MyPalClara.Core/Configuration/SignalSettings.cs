namespace MyPalClara.Core.Configuration;

public sealed class SignalSettings
{
    public string SignalCliPath { get; set; } = "signal-cli";
    public string AccountPhone { get; set; } = "";
    public List<string> AllowedNumbers { get; set; } = [];
    public int MaxMessageLength { get; set; } = 4096;
}
