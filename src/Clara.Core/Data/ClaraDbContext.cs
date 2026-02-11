using Clara.Core.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Clara.Core.Data;

public class ClaraDbContext : DbContext
{
    public ClaraDbContext(DbContextOptions<ClaraDbContext> options) : base(options) { }

    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<PlatformLinkEntity> PlatformLinks => Set<PlatformLinkEntity>();
    public DbSet<AdapterEntity> Adapters => Set<AdapterEntity>();
    public DbSet<ChannelEntity> Channels => Set<ChannelEntity>();
    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<LlmCallEntity> LlmCalls => Set<LlmCallEntity>();
    public DbSet<DiscordGuildEntity> DiscordGuilds => Set<DiscordGuildEntity>();
    public DbSet<DiscordChannelDetailEntity> DiscordChannelDetails => Set<DiscordChannelDetailEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Users
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.HasIndex(x => x.PrimaryEmail).IsUnique();
        });

        // PlatformLinks
        modelBuilder.Entity<PlatformLinkEntity>(e =>
        {
            e.HasIndex(x => x.PrefixedUserId).IsUnique();
            e.HasIndex(x => new { x.Platform, x.PlatformUserId }).IsUnique();
            e.HasOne(x => x.User)
                .WithMany(x => x.PlatformLinks)
                .HasForeignKey(x => x.UserId);
        });

        // Adapters
        modelBuilder.Entity<AdapterEntity>(e =>
        {
            e.HasMany(x => x.Channels)
                .WithOne(x => x.Adapter)
                .HasForeignKey(x => x.AdapterId);
        });

        // Channels
        modelBuilder.Entity<ChannelEntity>(e =>
        {
            e.HasIndex(x => new { x.AdapterId, x.ExternalId }).IsUnique();
            e.HasMany(x => x.Conversations)
                .WithOne(x => x.Channel)
                .HasForeignKey(x => x.ChannelId);
        });

        // Conversations
        modelBuilder.Entity<ConversationEntity>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.LastActivityAt });
            e.HasIndex(x => new { x.ChannelId, x.LastActivityAt });
            e.HasMany(x => x.Messages)
                .WithOne(x => x.Conversation)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Messages
        modelBuilder.Entity<MessageEntity>(e =>
        {
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
        });

        // LlmCalls
        modelBuilder.Entity<LlmCallEntity>(e =>
        {
            e.HasIndex(x => x.ConversationId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Model);
        });

        // DiscordGuilds
        modelBuilder.Entity<DiscordGuildEntity>(e =>
        {
            e.HasIndex(x => x.GuildId).IsUnique();
        });

        // DiscordChannelDetails
        modelBuilder.Entity<DiscordChannelDetailEntity>(e =>
        {
            e.HasOne(x => x.Channel)
                .WithOne()
                .HasForeignKey<DiscordChannelDetailEntity>(x => x.ChannelId);
            e.HasOne(x => x.Guild)
                .WithMany()
                .HasForeignKey(x => x.GuildId);
        });
    }
}
