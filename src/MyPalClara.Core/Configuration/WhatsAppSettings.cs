namespace MyPalClara.Core.Configuration;

public sealed class WhatsAppSettings
{
    public string? PhoneNumberId { get; set; }
    public string? AccessToken { get; set; }
    public string? VerifyToken { get; set; }
    public string? WebhookPath { get; set; } = "/webhook";
    public int WebhookPort { get; set; } = 5100;
    public int MaxMessageLength { get; set; } = 4096;
}
