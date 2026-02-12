namespace MyPalClara.Core.Configuration;

public sealed class SshSettings
{
    public int Port { get; set; } = 2222;
    public string? HostKey { get; set; }
}
