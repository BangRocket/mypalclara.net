namespace Clara.Core.Configuration;

public sealed class RookSettings
{
    public string Provider { get; set; } = "openai";
    public string Model { get; set; } = "gpt-4o-mini";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
}

public sealed class GraphStoreSettings
{
    public string Provider { get; set; } = "falkordb";
    public string FalkordbHost { get; set; } = "localhost";
    public int FalkordbPort { get; set; } = 6480;
    public string FalkordbPassword { get; set; } = "";
    public string FalkordbGraphName { get; set; } = "clara_memory";
    public int VectorDimension { get; set; } = 1536;
    public string SimilarityFunction { get; set; } = "cosine";
}

public sealed class EmbeddingSettings
{
    public string Provider { get; set; } = "ollama";
    public string BaseUrl { get; set; } = "http://localhost:11434/v1/";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "qwen3-embedding:4b";
    public bool CacheEnabled { get; set; } = true;
}

public sealed class MemorySettings
{
    public bool SkipProfileLoad { get; set; } = true;
    public string RedisUrl { get; set; } = "";

    public RookSettings Rook { get; set; } = new();
    public GraphStoreSettings GraphStore { get; set; } = new();
    public EmbeddingSettings Embedding { get; set; } = new();
}
