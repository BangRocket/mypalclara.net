using System.Collections.Concurrent;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Llm;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Agent.Llm;

/// <summary>
/// Creates and caches ILlmProvider instances per agent profile.
/// Falls back to the default DI-provided provider when no profile is specified.
/// </summary>
public sealed class LlmProviderFactory
{
    private readonly ILlmProvider _default;
    private readonly ClaraConfig _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly LlmCallLogger _callLogger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, ILlmProvider> _cache = new();

    public LlmProviderFactory(
        ILlmProvider defaultProvider,
        ClaraConfig config,
        IHttpClientFactory httpFactory,
        LlmCallLogger callLogger,
        ILoggerFactory loggerFactory)
    {
        _default = defaultProvider;
        _config = config;
        _httpFactory = httpFactory;
        _callLogger = callLogger;
        _loggerFactory = loggerFactory;
    }

    public ILlmProvider GetProvider(AgentProfile? profile)
    {
        if (profile is null || string.IsNullOrEmpty(profile.Provider))
            return _default;

        return _cache.GetOrAdd(profile.Name, _ => CreateProvider(profile));
    }

    private ILlmProvider CreateProvider(AgentProfile profile)
    {
        var isAnthropic = profile.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase);
        var httpClient = _httpFactory.CreateClient();

        if (isAnthropic)
            return new AnthropicProvider(httpClient, _config, _callLogger,
                _loggerFactory.CreateLogger<AnthropicProvider>());

        return new OpenAiProvider(httpClient, _config, _callLogger,
            _loggerFactory.CreateLogger<OpenAiProvider>());
    }
}
