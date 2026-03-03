using System.Collections.Concurrent;
using Clara.Core.Config;
using Clara.Core.Llm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clara.Core.Llm;

public class LlmProviderFactory : ILlmProviderFactory
{
    private readonly LlmOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, ILlmProvider> _providers = new();

    public LlmProviderFactory(IOptions<LlmOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
    }

    public ILlmProvider GetProvider(string? providerName = null)
    {
        var name = (providerName ?? _options.Provider).ToLowerInvariant();

        return _providers.GetOrAdd(name, CreateProvider);
    }

    public string ResolveModel(string providerName, ModelTier tier)
    {
        var opts = GetProviderOptions(providerName);

        var model = tier switch
        {
            ModelTier.High => opts.Models.High,
            ModelTier.Mid => opts.Models.Mid,
            ModelTier.Low => opts.Models.Low,
            _ => opts.Models.Mid
        };

        // Fall back to mid if the requested tier is not configured
        return model
               ?? opts.Models.Mid
               ?? GetDefaultModel(providerName, tier);
    }

    private ILlmProvider CreateProvider(string name)
    {
        var opts = GetProviderOptions(name);
        var apiKey = opts.ApiKey
                     ?? throw new InvalidOperationException($"No API key configured for provider '{name}'");

        return name switch
        {
            "anthropic" => new AnthropicProvider(
                apiKey,
                _loggerFactory.CreateLogger<AnthropicProvider>()),

            "openai" => new OpenAiCompatProvider(
                "openai",
                apiKey,
                opts.BaseUrl,
                ResolveModel(name, ModelTier.Mid),
                _loggerFactory.CreateLogger<OpenAiCompatProvider>()),

            "openrouter" => new OpenAiCompatProvider(
                "openrouter",
                apiKey,
                opts.BaseUrl ?? "https://openrouter.ai/api/v1",
                ResolveModel(name, ModelTier.Mid),
                _loggerFactory.CreateLogger<OpenAiCompatProvider>()),

            "nanogpt" => new OpenAiCompatProvider(
                "nanogpt",
                apiKey,
                opts.BaseUrl ?? "https://nano-gpt.com/api/v1",
                ResolveModel(name, ModelTier.Mid),
                _loggerFactory.CreateLogger<OpenAiCompatProvider>()),

            _ => new OpenAiCompatProvider(
                name,
                apiKey,
                opts.BaseUrl,
                ResolveModel(name, ModelTier.Mid),
                _loggerFactory.CreateLogger<OpenAiCompatProvider>())
        };
    }

    private ProviderOptions GetProviderOptions(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "anthropic" => _options.Anthropic,
            "openrouter" => _options.OpenRouter,
            "openai" => _options.OpenAi,
            "nanogpt" => _options.NanoGpt,
            _ => new ProviderOptions()
        };
    }

    private static string GetDefaultModel(string provider, ModelTier tier)
    {
        return provider.ToLowerInvariant() switch
        {
            "anthropic" => tier switch
            {
                ModelTier.High => "claude-sonnet-4-20250514",
                ModelTier.Mid => "claude-haiku-4-20250414",
                ModelTier.Low => "claude-haiku-4-20250414",
                _ => "claude-haiku-4-20250414"
            },
            "openai" => tier switch
            {
                ModelTier.High => "gpt-4o",
                ModelTier.Mid => "gpt-4o-mini",
                ModelTier.Low => "gpt-4o-mini",
                _ => "gpt-4o-mini"
            },
            "openrouter" => tier switch
            {
                ModelTier.High => "anthropic/claude-sonnet-4-20250514",
                ModelTier.Mid => "anthropic/claude-haiku-4-20250414",
                ModelTier.Low => "openai/gpt-4o-mini",
                _ => "anthropic/claude-haiku-4-20250414"
            },
            _ => "gpt-4o-mini"
        };
    }
}
