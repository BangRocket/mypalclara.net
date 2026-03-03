using Clara.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clara.Core.Data;

public class ClaraDbContext : DbContext
{
    public ClaraDbContext(DbContextOptions<ClaraDbContext> options) : base(options) { }

    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<MemoryEntity> Memories => Set<MemoryEntity>();
    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();
    public DbSet<McpServerEntity> McpServers => Set<McpServerEntity>();
    public DbSet<EmailAccountEntity> EmailAccounts => Set<EmailAccountEntity>();
    public DbSet<ToolUsageEntity> ToolUsages => Set<ToolUsageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isPostgres = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

        modelBuilder.Entity<UserEntity>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            if (isPostgres) e.Property(x => x.Preferences).HasColumnType("jsonb");
        });

        modelBuilder.Entity<SessionEntity>(e =>
        {
            e.ToTable("sessions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SessionKey);
            if (isPostgres) e.Property(x => x.Metadata).HasColumnType("jsonb");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasMany(x => x.Messages).WithOne(x => x.Session).HasForeignKey(x => x.SessionId);
        });

        modelBuilder.Entity<MessageEntity>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SessionId);
            if (isPostgres)
            {
                e.Property(x => x.ToolCalls).HasColumnType("jsonb");
                e.Property(x => x.ToolResults).HasColumnType("jsonb");
                e.Property(x => x.Metadata).HasColumnType("jsonb");
            }
        });

        modelBuilder.Entity<MemoryEntity>(e =>
        {
            e.ToTable("memories");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            if (isPostgres)
            {
                e.Property(x => x.Embedding).HasColumnType("vector(1536)");
                e.Property(x => x.Metadata).HasColumnType("jsonb");
            }
            else
            {
                e.Ignore(x => x.Embedding);
            }
        });

        modelBuilder.Entity<ProjectEntity>(e =>
        {
            e.ToTable("projects");
            e.HasKey(x => x.Id);
            if (isPostgres) e.Property(x => x.Metadata).HasColumnType("jsonb");
        });

        modelBuilder.Entity<McpServerEntity>(e =>
        {
            e.ToTable("mcp_servers");
            e.HasKey(x => x.Id);
            if (isPostgres)
            {
                e.Property(x => x.Env).HasColumnType("jsonb");
                e.Property(x => x.OAuthConfig).HasColumnType("jsonb");
            }
        });

        modelBuilder.Entity<EmailAccountEntity>(e =>
        {
            e.ToTable("email_accounts");
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<ToolUsageEntity>(e =>
        {
            e.ToTable("tool_usages");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ToolName);
            if (isPostgres) e.Property(x => x.Arguments).HasColumnType("jsonb");
        });
    }
}
