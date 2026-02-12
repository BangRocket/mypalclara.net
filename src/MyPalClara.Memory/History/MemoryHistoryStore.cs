using MyPalClara.Core.Data;
using MyPalClara.Core.Data.Models;
using MyPalClara.Core.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory.History;

/// <summary>
/// PostgreSQL-backed change log for memory operations via EF Core.
/// </summary>
public sealed class MemoryHistoryStore
{
    private readonly IDbContextFactory<ClaraDbContext> _dbFactory;
    private readonly ILogger<MemoryHistoryStore> _logger;

    public MemoryHistoryStore(IDbContextFactory<ClaraDbContext> dbFactory, ILogger<MemoryHistoryStore> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task AddEntryAsync(
        string memoryId, string? oldMemory, string? newMemory, string eventType,
        string? userId = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.MemoryHistory.Add(new MemoryHistoryEntity
        {
            MemoryId = memoryId,
            OldMemory = oldMemory,
            NewMemory = newMemory,
            Event = eventType,
            UserId = userId,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<MemoryHistoryEntry>> GetHistoryAsync(
        string memoryId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.MemoryHistory
            .Where(h => h.MemoryId == memoryId)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new MemoryHistoryEntry(
                h.Id.ToString(),
                h.MemoryId,
                h.UserId,
                h.OldMemory,
                h.NewMemory,
                h.Event,
                h.CreatedAt))
            .ToListAsync(ct);
    }
}
