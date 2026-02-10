using Clara.Core.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Clara.Core.Data;

/// <summary>EF Core context for Clara's FSRS and memory tracking tables.</summary>
public class ClaraDbContext : DbContext
{
    public ClaraDbContext(DbContextOptions<ClaraDbContext> options) : base(options) { }

    public DbSet<MemoryDynamicsEntity> MemoryDynamics => Set<MemoryDynamicsEntity>();
    public DbSet<MemoryAccessLogEntity> MemoryAccessLog => Set<MemoryAccessLogEntity>();
    public DbSet<MemorySupersessionEntity> MemorySupersessions => Set<MemorySupersessionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // MemoryDynamics
        modelBuilder.Entity<MemoryDynamicsEntity>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.LastAccessedAt });
            e.HasMany(x => x.AccessLogs)
                .WithOne(x => x.MemoryDynamics)
                .HasForeignKey(x => x.MemoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MemoryAccessLog
        modelBuilder.Entity<MemoryAccessLogEntity>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.AccessedAt });
        });

        // MemorySupersession
        modelBuilder.Entity<MemorySupersessionEntity>(e =>
        {
            e.HasIndex(x => x.OldMemoryId);
            e.HasIndex(x => x.NewMemoryId);
            e.HasIndex(x => x.UserId);
        });
    }
}
