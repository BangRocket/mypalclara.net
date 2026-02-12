namespace MyPalClara.Core.Configuration;

public sealed class ProviderSettings
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ModelHigh { get; set; } = "";
    public string ModelMid { get; set; } = "";
    public string ModelLow { get; set; } = "";

    // OpenRouter-specific
    public string Site { get; set; } = "";
    public string Title { get; set; } = "";

    // Bedrock-specific
    public string AwsRegion { get; set; } = "";
    public string AwsAccessKeyId { get; set; } = "";
    public string AwsSecretAccessKey { get; set; } = "";

    // Azure-specific
    public string Endpoint { get; set; } = "";
    public string DeploymentName { get; set; } = "";
    public string ApiVersion { get; set; } = "";
}

public sealed class CloudflareAccessSettings
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

public sealed class AutoTierSettings
{
    public bool Enabled { get; set; }
    public string DefaultTier { get; set; } = "high";
}

public sealed class LlmSettings
{
    public string Provider { get; set; } = "anthropic";
    public string OpenaiApiKey { get; set; } = "";

    public ProviderSettings Anthropic { get; set; } = new();
    public ProviderSettings Openai { get; set; } = new();
    public ProviderSettings Openrouter { get; set; } = new();
    public ProviderSettings Nanogpt { get; set; } = new();
    public ProviderSettings Bedrock { get; set; } = new();
    public ProviderSettings Azure { get; set; } = new();

    public CloudflareAccessSettings CloudflareAccess { get; set; } = new();
    public AutoTierSettings AutoTier { get; set; } = new();

    /// <summary>Returns the active provider settings based on <see cref="Provider"/>.</summary>
    public ProviderSettings ActiveProvider => Provider.ToLowerInvariant() switch
    {
        "anthropic" => Anthropic,
        "openai" => Openai,
        "openrouter" => Openrouter,
        "nanogpt" => Nanogpt,
        "bedrock" => Bedrock,
        "azure" => Azure,
        _ => Anthropic,
    };

    /// <summary>Resolves the model name for a given tier.</summary>
    public string ModelForTier(string? tier)
    {
        var p = ActiveProvider;
        return tier?.ToLowerInvariant() switch
        {
            "high" or "opus" => !string.IsNullOrEmpty(p.ModelHigh) ? p.ModelHigh : p.Model,
            "mid" or "sonnet" => !string.IsNullOrEmpty(p.ModelMid) ? p.ModelMid : p.Model,
            "low" or "haiku" or "fast" => !string.IsNullOrEmpty(p.ModelLow) ? p.ModelLow : p.Model,
            _ => p.Model,
        };
    }
}
