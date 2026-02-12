namespace MyPalClara.Core.Configuration;

public sealed class VoiceSettings
{
    public SttSettings Stt { get; set; } = new();
    public TtsSettings Tts { get; set; } = new();
    public VadSettings Vad { get; set; } = new();
    public bool EnableInterruption { get; set; } = true;
}

public sealed class SttSettings
{
    public string Provider { get; set; } = "openai";
    public string Model { get; set; } = "whisper-1";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
}

public sealed class TtsSettings
{
    public string Provider { get; set; } = "replicate";
    public string ReplicateApiToken { get; set; } = "";
    public string Speaker { get; set; } = "Chelsie";
    public string Language { get; set; } = "en";
}

public sealed class VadSettings
{
    public int Aggressiveness { get; set; } = 2;
    public double SilenceDuration { get; set; } = 0.8;
}
