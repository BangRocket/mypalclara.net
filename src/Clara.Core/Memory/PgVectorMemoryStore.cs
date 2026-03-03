using System.Text.Json;
using Clara.Core.Data;
using Clara.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace Clara.Core.Memory;

public class PgVectorMemoryStore : IMemoryStore
{
    private readonly ClaraDbContext _db;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly ILogger<PgVectorMemoryStore> _logger;
    private readonly bool _isPostgres;

    public PgVectorMemoryStore(
        ClaraDbContext db,
        ILogger<PgVectorMemoryStore> logger,
        IEmbeddingProvider? embeddingProvider = null)
    {
        _db = db;
        _logger = logger;
        _embeddingProvider = embeddingProvider;
        _isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    }

    public async Task StoreAsync(string userId, string content, MemoryMetadata? metadata = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var entity = new MemoryEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = content,
            Category = metadata?.Category,
            Score = 1.0f,
            Metadata = metadata?.Tags is not null ? JsonSerializer.Serialize(metadata.Tags) : null,
            CreatedAt = now,
            UpdatedAt = now,
            AccessCount = 0,
        };

        // Generate embedding if provider is available and we're using PostgreSQL
        if (_embeddingProvider is not null && _isPostgres)
        {
            try
            {
                var embedding = await _embeddingProvider.GetEmbeddingAsync(content, ct);
                entity.Embedding = new Vector(embedding);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate embedding for memory, storing without vector");
            }
        }

        _db.Memories.Add(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        string userId, string query, int limit = 10, CancellationToken ct = default)
    {
        // For SQLite: fall back to text matching (case-insensitive contains)
        // For PostgreSQL with embeddings: could do vector similarity search
        var memories = await _db.Memories
            .Where(m => m.UserId == userId)
            .Where(m => EF.Functions.Like(m.Content, $"%{query}%"))
            .OrderByDescending(m => m.UpdatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return memories.Select(m => new MemorySearchResult(
            ToEntry(m),
            CalculateTextRelevance(m.Content, query)
        )).ToList();
    }

    public async Task<IReadOnlyList<MemoryEntry>> GetAllAsync(string userId, CancellationToken ct = default)
    {
        var memories = await _db.Memories
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.UpdatedAt)
            .ToListAsync(ct);

        return memories.Select(ToEntry).ToList();
    }

    public async Task DeleteAsync(string userId, Guid memoryId, CancellationToken ct = default)
    {
        var entity = await _db.Memories
            .FirstOrDefaultAsync(m => m.Id == memoryId && m.UserId == userId, ct);

        if (entity is not null)
        {
            _db.Memories.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static MemoryEntry ToEntry(MemoryEntity entity) =>
        new(entity.Id, entity.UserId, entity.Content, entity.Category,
            entity.Score, entity.CreatedAt, entity.UpdatedAt);

    private static float CalculateTextRelevance(string content, string query)
    {
        // Simple relevance: ratio of query terms found in content
        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryTerms.Length == 0) return 0f;

        var matches = queryTerms.Count(term =>
            content.Contains(term, StringComparison.OrdinalIgnoreCase));

        return (float)matches / queryTerms.Length;
    }
}
