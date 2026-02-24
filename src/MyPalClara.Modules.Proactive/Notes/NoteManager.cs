using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Data;
using MyPalClara.Data.Entities;

namespace MyPalClara.Modules.Proactive.Notes;

public class NoteManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NoteManager> _logger;

    public NoteManager(IServiceScopeFactory scopeFactory, ILogger<NoteManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<string> CreateNoteAsync(string userId, string content, string noteType,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var note = new ProactiveNote
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Note = content,
            NoteType = noteType,
            RelevanceScore = 100,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.ProactiveNotes.Add(note);
        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Created {Type} note for {User}: {Id}", noteType, userId, note.Id);
        return note.Id;
    }

    public async Task<List<ProactiveNote>> GetActiveNotesAsync(string userId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        return await db.ProactiveNotes
            .Where(n => n.UserId == userId && n.Archived == "false")
            .OrderByDescending(n => n.RelevanceScore)
            .Take(20)
            .ToListAsync(ct);
    }

    public async Task DecayStaleNotesAsync(int decayDays, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var cutoff = DateTime.UtcNow.AddDays(-decayDays);
        var stale = await db.ProactiveNotes
            .Where(n => n.Archived == "false" && n.UpdatedAt < cutoff)
            .ToListAsync(ct);

        foreach (var note in stale)
        {
            note.Archived = "true";
            note.UpdatedAt = DateTime.UtcNow;
        }

        if (stale.Count > 0) await db.SaveChangesAsync(ct);
        _logger.LogDebug("Decayed {Count} stale notes", stale.Count);
    }
}
