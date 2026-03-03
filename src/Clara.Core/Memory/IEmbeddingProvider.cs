namespace Clara.Core.Memory;

public interface IEmbeddingProvider
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
}
