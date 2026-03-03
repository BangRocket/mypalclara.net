namespace Clara.Core.Config;

public class LlmOptions
{
    public string Provider { get; set; } = "anthropic";
    public string DefaultTier { get; set; } = "mid";
    public bool AutoTierSelection { get; set; } = true;
    public ProviderOptions Anthropic { get; set; } = new();
    public ProviderOptions OpenRouter { get; set; } = new();
    public ProviderOptions OpenAi { get; set; } = new();
    public ProviderOptions NanoGpt { get; set; } = new();
}

public class ProviderOptions
{
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public ModelTierOptions Models { get; set; } = new();
}

public class ModelTierOptions
{
    public string? High { get; set; }
    public string? Mid { get; set; }
    public string? Low { get; set; }
}
