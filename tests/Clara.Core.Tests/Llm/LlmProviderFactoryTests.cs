using Clara.Core.Config;
using Clara.Core.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clara.Core.Tests.Llm;

public class LlmProviderFactoryTests
{
    private static LlmProviderFactory CreateFactory(LlmOptions? options = null)
    {
        options ??= new LlmOptions
        {
            Provider = "anthropic",
            Anthropic = new ProviderOptions
            {
                ApiKey = "test-key",
                Models = new ModelTierOptions
                {
                    High = "claude-sonnet-4-20250514",
                    Mid = "claude-haiku-4-20250414",
                    Low = "claude-haiku-4-20250414"
                }
            },
            OpenAi = new ProviderOptions
            {
                ApiKey = "test-openai-key",
                Models = new ModelTierOptions
                {
                    High = "gpt-4o",
                    Mid = "gpt-4o-mini",
                    Low = "gpt-4o-mini"
                }
            }
        };

        return new LlmProviderFactory(
            Options.Create(options),
            NullLoggerFactory.Instance);
    }

    [Fact]
    public void GetProvider_ReturnsDefaultProvider()
    {
        var factory = CreateFactory();
        var provider = factory.GetProvider();

        Assert.Equal("anthropic", provider.Name);
    }

    [Fact]
    public void GetProvider_ReturnsNamedProvider()
    {
        var factory = CreateFactory();
        var provider = factory.GetProvider("openai");

        Assert.Equal("openai", provider.Name);
    }

    [Fact]
    public void GetProvider_CachesInstances()
    {
        var factory = CreateFactory();
        var p1 = factory.GetProvider("anthropic");
        var p2 = factory.GetProvider("anthropic");

        Assert.Same(p1, p2);
    }

    [Fact]
    public void ResolveModel_ReturnsConfiguredModel()
    {
        var factory = CreateFactory();
        var model = factory.ResolveModel("anthropic", ModelTier.High);

        Assert.Equal("claude-sonnet-4-20250514", model);
    }

    [Fact]
    public void ResolveModel_FallsBackToMid()
    {
        var options = new LlmOptions
        {
            Anthropic = new ProviderOptions
            {
                ApiKey = "test",
                Models = new ModelTierOptions { Mid = "claude-mid-model" }
            }
        };
        var factory = CreateFactory(options);

        // High is not configured, should fall back to Mid
        var model = factory.ResolveModel("anthropic", ModelTier.High);
        Assert.Equal("claude-mid-model", model);
    }

    [Fact]
    public void ResolveModel_FallsBackToDefault()
    {
        var options = new LlmOptions
        {
            Anthropic = new ProviderOptions
            {
                ApiKey = "test",
                Models = new ModelTierOptions() // no models configured
            }
        };
        var factory = CreateFactory(options);

        var model = factory.ResolveModel("anthropic", ModelTier.High);
        // Should fall back to the built-in default
        Assert.NotNull(model);
        Assert.NotEmpty(model);
    }

    [Fact]
    public void GetProvider_ThrowsWhenNoApiKey()
    {
        var options = new LlmOptions
        {
            Provider = "anthropic",
            Anthropic = new ProviderOptions { ApiKey = null }
        };
        var factory = CreateFactory(options);

        Assert.Throws<InvalidOperationException>(() => factory.GetProvider());
    }
}
