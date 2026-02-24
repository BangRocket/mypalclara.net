namespace MyPalClara.Memory.FactExtraction;

public interface IFactExtractor
{
    Task<List<ExtractedFact>> ExtractAsync(
        string userMessage,
        string assistantMessage,
        string userId,
        CancellationToken ct = default);
}

public record ExtractedFact(string Text, bool IsKey);
