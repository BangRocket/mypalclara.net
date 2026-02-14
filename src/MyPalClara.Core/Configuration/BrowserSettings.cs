namespace MyPalClara.Core.Configuration;

public sealed class BrowserSettings
{
    public bool Enabled { get; set; }
    public bool Headless { get; set; } = true;
    public string BrowserType { get; set; } = "chromium";
    public string? UserDataDir { get; set; }
    public int DefaultTimeoutMs { get; set; } = 30000;
}
