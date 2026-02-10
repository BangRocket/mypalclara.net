namespace Clara.Core.Configuration;

public sealed class RookSettings
{
    public string Provider { get; set; } = "openai";
    public string Model { get; set; } = "gpt-4o-mini";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
}

public sealed class VectorStoreSettings
{
    public string DatabaseUrl { get; set; } = "";
    public string QdrantUrl { get; set; } = "";
    public string QdrantApiKey { get; set; } = "";
    public string CollectionName { get; set; } = "clara_memories";
    public string MigrationMode { get; set; } = "primary_only";
}

public sealed class GraphStoreSettings
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "falkordb";
    public string FalkordbHost { get; set; } = "localhost";
    public int FalkordbPort { get; set; } = 6379;
    public string FalkordbPassword { get; set; } = "";
    public string FalkordbGraphName { get; set; } = "clara_memory";
}

public sealed class EmbeddingSettings
{
    public string Model { get; set; } = "text-embedding-3-small";
    public bool CacheEnabled { get; set; } = true;
}

public sealed class MemorySettings
{
    public bool SkipProfileLoad { get; set; } = true;
    public string RedisUrl { get; set; } = "";

    public RookSettings Rook { get; set; } = new();
    public VectorStoreSettings VectorStore { get; set; } = new();
    public GraphStoreSettings GraphStore { get; set; } = new();
    public EmbeddingSettings Embedding { get; set; } = new();
}
