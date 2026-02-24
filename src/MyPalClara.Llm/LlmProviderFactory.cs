using MyPalClara.Llm.Providers;

namespace MyPalClara.Llm;

public static class LlmProviderFactory
{
    public static ILlmProvider Create(LlmConfig config, HttpClient httpClient)
    {
        return config.Provider.ToLowerInvariant() switch
        {
            "anthropic" => new AnthropicProvider(config, httpClient),
            "openrouter" or "nanogpt" or "openai" or "azure" =>
                new OpenAiCompatibleProvider(config, httpClient),
            _ => throw new InvalidOperationException(
                $"Unknown LLM provider: '{config.Provider}'. " +
                "Supported: anthropic, openrouter, nanogpt, openai, azure")
        };
    }
}
