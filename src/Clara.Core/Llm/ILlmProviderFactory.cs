namespace Clara.Core.Llm;

public interface ILlmProviderFactory
{
    ILlmProvider GetProvider(string? providerName = null);
    string ResolveModel(string providerName, ModelTier tier);
}
