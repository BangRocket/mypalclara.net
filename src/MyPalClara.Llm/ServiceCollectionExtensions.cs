using Microsoft.Extensions.DependencyInjection;

namespace MyPalClara.Llm;

public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddClaraLlm(this IServiceCollection services)
    {
        services.AddHttpClient("ClaraLlm");

        services.AddSingleton<LlmConfig>(sp => LlmConfig.FromEnvironment());

        services.AddSingleton<ILlmProvider>(sp =>
            LlmProviderFactory.Create(
                sp.GetRequiredService<LlmConfig>(),
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ClaraLlm")));

        return services;
    }
}
