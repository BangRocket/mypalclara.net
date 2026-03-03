namespace Clara.Core.Config;

public class MemoryOptions
{
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string? EmbeddingApiKey { get; set; }
    public int SearchLimit { get; set; } = 10;
    public string ExtractionModel { get; set; } = "gpt-4o-mini";
    public int SessionIdleMinutes { get; set; } = 30;
    public bool EnableGraphMemory { get; set; }
}
