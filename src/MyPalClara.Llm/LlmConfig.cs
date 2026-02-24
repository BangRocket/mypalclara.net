namespace MyPalClara.Llm;

public class LlmConfig
{
    public string Provider { get; set; } = "anthropic";
    public string Model { get; set; } = "claude-sonnet-4-5";
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public int MaxTokens { get; set; } = 4096;
    public float Temperature { get; set; } = 0.0f;
    public ModelTier? Tier { get; set; }
    public Dictionary<string, string>? ExtraHeaders { get; set; }

    // Azure-specific fields
    public string? AzureDeploymentName { get; set; }
    public string? AzureApiVersion { get; set; }

    public static LlmConfig FromEnvironment(ModelTier? tier = null)
    {
        var provider = GetEnv("LLM_PROVIDER", "anthropic").ToLowerInvariant();

        // Resolve tier: explicit parameter > MODEL_TIER env var > null
        var effectiveTier = tier ?? ParseTierFromEnv();

        var config = provider switch
        {
            "anthropic" => BuildAnthropicConfig(effectiveTier),
            "openrouter" => BuildOpenRouterConfig(effectiveTier),
            "nanogpt" => BuildNanoGptConfig(effectiveTier),
            "openai" => BuildOpenAiConfig(effectiveTier),
            "azure" => BuildAzureConfig(effectiveTier),
            _ => throw new InvalidOperationException(
                $"Unknown LLM provider: '{provider}'. " +
                "Supported: anthropic, openrouter, nanogpt, openai, azure")
        };

        config.Provider = provider;
        config.Tier = effectiveTier;

        // Cloudflare Access headers
        var cfClientId = GetEnvOrNull("CF_ACCESS_CLIENT_ID");
        var cfClientSecret = GetEnvOrNull("CF_ACCESS_CLIENT_SECRET");
        if (cfClientId is not null && cfClientSecret is not null)
        {
            config.ExtraHeaders ??= new Dictionary<string, string>();
            config.ExtraHeaders["CF-Access-Client-Id"] = cfClientId;
            config.ExtraHeaders["CF-Access-Client-Secret"] = cfClientSecret;
        }

        return config;
    }

    private static LlmConfig BuildAnthropicConfig(ModelTier? tier)
    {
        var model = ResolveTieredModel("ANTHROPIC_MODEL", "claude-sonnet-4-5", "ANTHROPIC", tier);
        return new LlmConfig
        {
            ApiKey = GetEnv("ANTHROPIC_API_KEY"),
            BaseUrl = GetEnvOrNull("ANTHROPIC_BASE_URL"),
            Model = model
        };
    }

    private static LlmConfig BuildOpenRouterConfig(ModelTier? tier)
    {
        var model = ResolveTieredModel("OPENROUTER_MODEL", "anthropic/claude-sonnet-4", "OPENROUTER", tier);
        var config = new LlmConfig
        {
            ApiKey = GetEnv("OPENROUTER_API_KEY"),
            BaseUrl = "https://openrouter.ai/api/v1",
            Model = model
        };

        var site = GetEnvOrNull("OPENROUTER_SITE");
        var title = GetEnvOrNull("OPENROUTER_TITLE");
        if (site is not null || title is not null)
        {
            config.ExtraHeaders = new Dictionary<string, string>();
            if (site is not null) config.ExtraHeaders["HTTP-Referer"] = site;
            if (title is not null) config.ExtraHeaders["X-Title"] = title;
        }

        return config;
    }

    private static LlmConfig BuildNanoGptConfig(ModelTier? tier)
    {
        var model = ResolveTieredModel("NANOGPT_MODEL", "moonshotai/Kimi-K2-Instruct-0905", "NANOGPT", tier);
        return new LlmConfig
        {
            ApiKey = GetEnv("NANOGPT_API_KEY"),
            BaseUrl = "https://nano-gpt.com/api/v1",
            Model = model
        };
    }

    private static LlmConfig BuildOpenAiConfig(ModelTier? tier)
    {
        var model = ResolveTieredModel("CUSTOM_OPENAI_MODEL", "gpt-4o", "CUSTOM_OPENAI", tier);
        return new LlmConfig
        {
            ApiKey = GetEnv("CUSTOM_OPENAI_API_KEY"),
            BaseUrl = GetEnv("CUSTOM_OPENAI_BASE_URL", "https://api.openai.com/v1"),
            Model = model
        };
    }

    private static LlmConfig BuildAzureConfig(ModelTier? tier)
    {
        var model = ResolveTieredModel("AZURE_MODEL", "gpt-4o", "AZURE", tier);
        return new LlmConfig
        {
            ApiKey = GetEnv("AZURE_OPENAI_API_KEY"),
            BaseUrl = GetEnv("AZURE_OPENAI_ENDPOINT"),
            Model = model,
            AzureDeploymentName = GetEnv("AZURE_DEPLOYMENT_NAME"),
            AzureApiVersion = GetEnv("AZURE_API_VERSION", "2024-02-15-preview")
        };
    }

    /// <summary>
    /// Resolve the model name, preferring a tier-specific override if a tier is set.
    /// E.g., for prefix "ANTHROPIC" and tier High, checks ANTHROPIC_MODEL_HIGH first.
    /// </summary>
    private static string ResolveTieredModel(string baseEnvVar, string defaultModel, string prefix, ModelTier? tier)
    {
        if (tier is not null)
        {
            var tierSuffix = tier.Value switch
            {
                ModelTier.High => "HIGH",
                ModelTier.Mid => "MID",
                ModelTier.Low => "LOW",
                _ => throw new ArgumentOutOfRangeException(nameof(tier))
            };
            var tieredModel = GetEnvOrNull($"{prefix}_MODEL_{tierSuffix}");
            if (tieredModel is not null)
                return tieredModel;
        }

        return GetEnv(baseEnvVar, defaultModel);
    }

    private static ModelTier? ParseTierFromEnv()
    {
        var tierStr = GetEnvOrNull("MODEL_TIER");
        if (tierStr is null) return null;

        return tierStr.ToLowerInvariant() switch
        {
            "high" => ModelTier.High,
            "mid" => ModelTier.Mid,
            "low" => ModelTier.Low,
            _ => null
        };
    }

    private static string GetEnv(string name, string? defaultValue = null)
    {
        return Environment.GetEnvironmentVariable(name)
               ?? defaultValue
               ?? throw new InvalidOperationException(
                   $"Required environment variable '{name}' is not set.");
    }

    private static string? GetEnvOrNull(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }
}
