using MyPalClara.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Data;

public class ClaraDbContext : DbContext
{
    private readonly string? _connectionString;

    public ClaraDbContext(DbContextOptions<ClaraDbContext> options) : base(options) { }

    public ClaraDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    // Core
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ChannelSummary> ChannelSummaries => Set<ChannelSummary>();
    public DbSet<ChannelConfig> ChannelConfigs => Set<ChannelConfig>();
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();
    public DbSet<GoogleOAuthToken> GoogleOAuthTokens => Set<GoogleOAuthToken>();

    // Proactive
    public DbSet<ProactiveMessage> ProactiveMessages => Set<ProactiveMessage>();
    public DbSet<UserInteractionPattern> UserInteractionPatterns => Set<UserInteractionPattern>();
    public DbSet<ProactiveNote> ProactiveNotes => Set<ProactiveNote>();
    public DbSet<ProactiveAssessment> ProactiveAssessments => Set<ProactiveAssessment>();

    // Email
    public DbSet<EmailAccount> EmailAccounts => Set<EmailAccount>();
    public DbSet<EmailRule> EmailRules => Set<EmailRule>();
    public DbSet<EmailAlert> EmailAlerts => Set<EmailAlert>();

    // Guild
    public DbSet<GuildConfig> GuildConfigs => Set<GuildConfig>();

    // Memory
    public DbSet<MemoryDynamics> MemoryDynamics => Set<MemoryDynamics>();
    public DbSet<MemoryAccessLog> MemoryAccessLogs => Set<MemoryAccessLog>();
    public DbSet<Intention> Intentions => Set<Intention>();
    public DbSet<MemorySupersession> MemorySupersessions => Set<MemorySupersession>();
    public DbSet<MemoryHistory> MemoryHistories => Set<MemoryHistory>();

    // Identity
    public DbSet<CanonicalUser> CanonicalUsers => Set<CanonicalUser>();
    public DbSet<PlatformLink> PlatformLinks => Set<PlatformLink>();
    public DbSet<OAuthToken> OAuthTokens => Set<OAuthToken>();
    public DbSet<WebSession> WebSessions => Set<WebSession>();

    // Audit
    public DbSet<ToolAuditLog> ToolAuditLogs => Set<ToolAuditLog>();

    // Personality
    public DbSet<PersonalityTrait> PersonalityTraits => Set<PersonalityTrait>();
    public DbSet<PersonalityTraitHistory> PersonalityTraitHistories => Set<PersonalityTraitHistory>();

    // MCP
    public DbSet<McpServer> McpServers => Set<McpServer>();
    public DbSet<McpOAuthToken> McpOAuthTokens => Set<McpOAuthToken>();
    public DbSet<McpToolCall> McpToolCalls => Set<McpToolCall>();
    public DbSet<McpUsageMetrics> McpUsageMetrics => Set<McpUsageMetrics>();
    public DbSet<McpRateLimit> McpRateLimits => Set<McpRateLimit>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
            return;

        if (_connectionString is not null)
        {
            if (_connectionString.Contains("postgresql") || _connectionString.Contains("postgres"))
                optionsBuilder.UseNpgsql(_connectionString);
            else
                optionsBuilder.UseSqlite(_connectionString);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureProject(modelBuilder);
        ConfigureSession(modelBuilder);
        ConfigureMessage(modelBuilder);
        ConfigureChannelSummary(modelBuilder);
        ConfigureChannelConfig(modelBuilder);
        ConfigureLogEntry(modelBuilder);
        ConfigureGoogleOAuthToken(modelBuilder);
        ConfigureProactiveMessage(modelBuilder);
        ConfigureUserInteractionPattern(modelBuilder);
        ConfigureProactiveNote(modelBuilder);
        ConfigureProactiveAssessment(modelBuilder);
        ConfigureEmailAccount(modelBuilder);
        ConfigureEmailRule(modelBuilder);
        ConfigureEmailAlert(modelBuilder);
        ConfigureGuildConfig(modelBuilder);
        ConfigureMemoryDynamics(modelBuilder);
        ConfigureMemoryAccessLog(modelBuilder);
        ConfigureIntention(modelBuilder);
        ConfigureMemorySupersession(modelBuilder);
        ConfigureMemoryHistory(modelBuilder);
        ConfigureCanonicalUser(modelBuilder);
        ConfigurePlatformLink(modelBuilder);
        ConfigureOAuthToken(modelBuilder);
        ConfigureWebSession(modelBuilder);
        ConfigureToolAuditLog(modelBuilder);
        ConfigurePersonalityTrait(modelBuilder);
        ConfigurePersonalityTraitHistory(modelBuilder);
        ConfigureMcpServer(modelBuilder);
        ConfigureMcpOAuthToken(modelBuilder);
        ConfigureMcpToolCall(modelBuilder);
        ConfigureMcpUsageMetrics(modelBuilder);
        ConfigureMcpRateLimit(modelBuilder);
    }

    private static void ConfigureProject(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });
    }

    private static void ConfigureSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ContextId).HasColumnName("context_id").HasDefaultValue("default");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Archived).HasColumnName("archived").HasDefaultValue("false");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.LastActivityAt).HasColumnName("last_activity_at");
            entity.Property(e => e.PreviousSessionId).HasColumnName("previous_session_id");
            entity.Property(e => e.ContextSnapshot).HasColumnName("context_snapshot").HasColumnType("TEXT");
            entity.Property(e => e.SessionSummary).HasColumnName("session_summary").HasColumnType("TEXT");

            entity.HasIndex(e => new { e.UserId, e.ContextId, e.ProjectId })
                .HasDatabaseName("ix_session_user_context_project");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Sessions)
                .HasForeignKey(e => e.ProjectId);
        });
    }

    private static void ConfigureMessage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Role).HasColumnName("role");
            entity.Property(e => e.Content).HasColumnName("content").HasColumnType("TEXT");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => new { e.SessionId, e.CreatedAt })
                .HasDatabaseName("ix_message_session_created");

            entity.HasOne(e => e.Session)
                .WithMany(s => s.Messages)
                .HasForeignKey(e => e.SessionId);
        });
    }

    private static void ConfigureChannelSummary(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChannelSummary>(entity =>
        {
            entity.ToTable("channel_summaries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChannelId).HasColumnName("channel_id");
            entity.Property(e => e.Summary).HasColumnName("summary").HasColumnType("TEXT").HasDefaultValue("");
            entity.Property(e => e.SummaryCutoffAt).HasColumnName("summary_cutoff_at");
            entity.Property(e => e.LastUpdatedAt).HasColumnName("last_updated_at");

            entity.HasIndex(e => e.ChannelId).IsUnique();
        });
    }

    private static void ConfigureChannelConfig(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChannelConfig>(entity =>
        {
            entity.ToTable("channel_configs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChannelId).HasColumnName("channel_id");
            entity.Property(e => e.GuildId).HasColumnName("guild_id");
            entity.Property(e => e.Mode).HasColumnName("mode").HasDefaultValue("mention");
            entity.Property(e => e.ConfiguredBy).HasColumnName("configured_by");
            entity.Property(e => e.ConfiguredAt).HasColumnName("configured_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.ChannelId).IsUnique();
            entity.HasIndex(e => e.GuildId);
        });
    }

    private static void ConfigureLogEntry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LogEntry>(entity =>
        {
            entity.ToTable("log_entries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Timestamp).HasColumnName("timestamp");
            entity.Property(e => e.Level).HasColumnName("level").HasMaxLength(10);
            entity.Property(e => e.LoggerName).HasColumnName("logger_name").HasMaxLength(100);
            entity.Property(e => e.Message).HasColumnName("message").HasColumnType("TEXT");
            entity.Property(e => e.Module).HasColumnName("module").HasMaxLength(100);
            entity.Property(e => e.Function).HasColumnName("function").HasMaxLength(100);
            entity.Property(e => e.LineNumber).HasColumnName("line_number");
            entity.Property(e => e.Exception).HasColumnName("exception").HasColumnType("TEXT");
            entity.Property(e => e.ExtraData).HasColumnName("extra_data").HasColumnType("TEXT");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");

            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Level);
            entity.HasIndex(e => e.LoggerName);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.SessionId);
        });
    }

    private static void ConfigureGoogleOAuthToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GoogleOAuthToken>(entity =>
        {
            entity.ToTable("google_oauth_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.AccessToken).HasColumnName("access_token").HasColumnType("TEXT");
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token").HasColumnType("TEXT");
            entity.Property(e => e.TokenType).HasColumnName("token_type").HasDefaultValue("Bearer");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.Scopes).HasColumnName("scopes").HasColumnType("TEXT");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.UserId).IsUnique();
        });
    }

    private static void ConfigureProactiveMessage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProactiveMessage>(entity =>
        {
            entity.ToTable("proactive_messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ChannelId).HasColumnName("channel_id");
            entity.Property(e => e.Message).HasColumnName("message").HasColumnType("TEXT");
            entity.Property(e => e.Priority).HasColumnName("priority");
            entity.Property(e => e.Reason).HasColumnName("reason").HasColumnType("TEXT");
            entity.Property(e => e.SentAt).HasColumnName("sent_at");
            entity.Property(e => e.ResponseReceived).HasColumnName("response_received").HasDefaultValue("false");
            entity.Property(e => e.ResponseAt).HasColumnName("response_at");

            entity.HasIndex(e => e.UserId);
        });
    }

    private static void ConfigureUserInteractionPattern(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserInteractionPattern>(entity =>
        {
            entity.ToTable("user_interaction_patterns");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.LastInteractionAt).HasColumnName("last_interaction_at");
            entity.Property(e => e.LastInteractionChannel).HasColumnName("last_interaction_channel");
            entity.Property(e => e.LastInteractionSummary).HasColumnName("last_interaction_summary").HasColumnType("TEXT");
            entity.Property(e => e.LastInteractionEnergy).HasColumnName("last_interaction_energy");
            entity.Property(e => e.TypicalActiveHours).HasColumnName("typical_active_hours").HasColumnType("TEXT");
            entity.Property(e => e.Timezone).HasColumnName("timezone");
            entity.Property(e => e.TimezoneSource).HasColumnName("timezone_source");
            entity.Property(e => e.AvgResponseTimeSeconds).HasColumnName("avg_response_time_seconds");
            entity.Property(e => e.ExplicitSignals).HasColumnName("explicit_signals").HasColumnType("TEXT");
            entity.Property(e => e.ProactiveSuccessRate).HasColumnName("proactive_success_rate");
            entity.Property(e => e.ProactiveResponseRate).HasColumnName("proactive_response_rate");
            entity.Property(e => e.PreferredProactiveTimes).HasColumnName("preferred_proactive_times").HasColumnType("TEXT");
            entity.Property(e => e.PreferredProactiveTypes).HasColumnName("preferred_proactive_types").HasColumnType("TEXT");
            entity.Property(e => e.TopicReceptiveness).HasColumnName("topic_receptiveness").HasColumnType("TEXT");
            entity.Property(e => e.ExplicitBoundaries).HasColumnName("explicit_boundaries").HasColumnType("TEXT");
            entity.Property(e => e.OpenThreads).HasColumnName("open_threads").HasColumnType("TEXT");
            entity.Property(e => e.ContactCadenceDays).HasColumnName("contact_cadence_days");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });
    }

    private static void ConfigureProactiveNote(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProactiveNote>(entity =>
        {
            entity.ToTable("proactive_notes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Note).HasColumnName("note").HasColumnType("TEXT");
            entity.Property(e => e.NoteType).HasColumnName("note_type");
            entity.Property(e => e.SourceContext).HasColumnName("source_context").HasColumnType("TEXT");
            entity.Property(e => e.SourceModel).HasColumnName("source_model");
            entity.Property(e => e.SourceConfidence).HasColumnName("source_confidence");
            entity.Property(e => e.GroundingMessageIds).HasColumnName("grounding_message_ids").HasColumnType("TEXT");
            entity.Property(e => e.Connections).HasColumnName("connections").HasColumnType("TEXT");
            entity.Property(e => e.RelevanceScore).HasColumnName("relevance_score").HasDefaultValue(100);
            entity.Property(e => e.SurfaceConditions).HasColumnName("surface_conditions").HasColumnType("TEXT");
            entity.Property(e => e.SurfaceAt).HasColumnName("surface_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.Surfaced).HasColumnName("surfaced").HasDefaultValue("false");
            entity.Property(e => e.SurfacedAt).HasColumnName("surfaced_at");
            entity.Property(e => e.Archived).HasColumnName("archived").HasDefaultValue("false");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.UserId);
        });
    }

    private static void ConfigureProactiveAssessment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProactiveAssessment>(entity =>
        {
            entity.ToTable("proactive_assessments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ContextSnapshot).HasColumnName("context_snapshot").HasColumnType("TEXT");
            entity.Property(e => e.Assessment).HasColumnName("assessment").HasColumnType("TEXT");
            entity.Property(e => e.Decision).HasColumnName("decision");
            entity.Property(e => e.Reasoning).HasColumnName("reasoning").HasColumnType("TEXT");
            entity.Property(e => e.NoteCreated).HasColumnName("note_created");
            entity.Property(e => e.MessageSent).HasColumnName("message_sent").HasColumnType("TEXT");
            entity.Property(e => e.NextCheckAt).HasColumnName("next_check_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAt);
        });
    }

    private static void ConfigureEmailAccount(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailAccount>(entity =>
        {
            entity.ToTable("email_accounts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.EmailAddress).HasColumnName("email_address");
            entity.Property(e => e.ProviderType).HasColumnName("provider_type");
            entity.Property(e => e.ImapServer).HasColumnName("imap_server");
            entity.Property(e => e.ImapPort).HasColumnName("imap_port");
            entity.Property(e => e.ImapUsername).HasColumnName("imap_username");
            entity.Property(e => e.ImapPassword).HasColumnName("imap_password").HasColumnType("TEXT");
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue("true");
            entity.Property(e => e.PollIntervalMinutes).HasColumnName("poll_interval_minutes").HasDefaultValue(5);
            entity.Property(e => e.LastCheckedAt).HasColumnName("last_checked_at");
            entity.Property(e => e.LastSeenUid).HasColumnName("last_seen_uid");
            entity.Property(e => e.LastSeenTimestamp).HasColumnName("last_seen_timestamp");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("active");
            entity.Property(e => e.LastError).HasColumnName("last_error").HasColumnType("TEXT");
            entity.Property(e => e.ErrorCount).HasColumnName("error_count").HasDefaultValue(0);
            entity.Property(e => e.AlertChannelId).HasColumnName("alert_channel_id");
            entity.Property(e => e.PingOnAlert).HasColumnName("ping_on_alert").HasDefaultValue("false");
            entity.Property(e => e.QuietHoursStart).HasColumnName("quiet_hours_start");
            entity.Property(e => e.QuietHoursEnd).HasColumnName("quiet_hours_end");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.UserId);
        });
    }

    private static void ConfigureEmailRule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailRule>(entity =>
        {
            entity.ToTable("email_rules");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue("true");
            entity.Property(e => e.Priority).HasColumnName("priority").HasDefaultValue(0);
            entity.Property(e => e.RuleDefinition).HasColumnName("rule_definition").HasColumnType("TEXT");
            entity.Property(e => e.Importance).HasColumnName("importance").HasDefaultValue("normal");
            entity.Property(e => e.CustomAlertMessage).HasColumnName("custom_alert_message").HasColumnType("TEXT");
            entity.Property(e => e.OverridePing).HasColumnName("override_ping");
            entity.Property(e => e.PresetName).HasColumnName("preset_name");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.Account)
                .WithMany(a => a.EmailRules)
                .HasForeignKey(e => e.AccountId);
        });
    }

    private static void ConfigureEmailAlert(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailAlert>(entity =>
        {
            entity.ToTable("email_alerts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.RuleId).HasColumnName("rule_id");
            entity.Property(e => e.EmailUid).HasColumnName("email_uid");
            entity.Property(e => e.EmailFrom).HasColumnName("email_from");
            entity.Property(e => e.EmailSubject).HasColumnName("email_subject");
            entity.Property(e => e.EmailSnippet).HasColumnName("email_snippet").HasColumnType("TEXT");
            entity.Property(e => e.EmailReceivedAt).HasColumnName("email_received_at");
            entity.Property(e => e.ChannelId).HasColumnName("channel_id");
            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.Importance).HasColumnName("importance");
            entity.Property(e => e.WasPinged).HasColumnName("was_pinged").HasDefaultValue("false");
            entity.Property(e => e.SentAt).HasColumnName("sent_at");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.EmailUid);

            entity.HasOne(e => e.Account)
                .WithMany(a => a.EmailAlerts)
                .HasForeignKey(e => e.AccountId);

            entity.HasOne(e => e.Rule)
                .WithMany(r => r.EmailAlerts)
                .HasForeignKey(e => e.RuleId);
        });
    }

    private static void ConfigureGuildConfig(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuildConfig>(entity =>
        {
            entity.ToTable("guild_configs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.GuildId).HasColumnName("guild_id");
            entity.Property(e => e.DefaultTier).HasColumnName("default_tier");
            entity.Property(e => e.AutoTierEnabled).HasColumnName("auto_tier_enabled").HasDefaultValue("false");
            entity.Property(e => e.OrsEnabled).HasColumnName("ors_enabled").HasDefaultValue("false");
            entity.Property(e => e.OrsChannelId).HasColumnName("ors_channel_id");
            entity.Property(e => e.OrsQuietStart).HasColumnName("ors_quiet_start");
            entity.Property(e => e.OrsQuietEnd).HasColumnName("ors_quiet_end");
            entity.Property(e => e.SandboxMode).HasColumnName("sandbox_mode").HasDefaultValue("auto");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.GuildId).IsUnique();
        });
    }

    private static void ConfigureMemoryDynamics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryDynamics>(entity =>
        {
            entity.ToTable("memory_dynamics");
            entity.HasKey(e => e.MemoryId);
            entity.Property(e => e.MemoryId).HasColumnName("memory_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Stability).HasColumnName("stability").HasDefaultValue(1.0);
            entity.Property(e => e.Difficulty).HasColumnName("difficulty").HasDefaultValue(5.0);
            entity.Property(e => e.RetrievalStrength).HasColumnName("retrieval_strength").HasDefaultValue(1.0);
            entity.Property(e => e.StorageStrength).HasColumnName("storage_strength").HasDefaultValue(0.5);
            entity.Property(e => e.IsKey).HasColumnName("is_key").HasDefaultValue(false);
            entity.Property(e => e.ImportanceWeight).HasColumnName("importance_weight").HasDefaultValue(1.0);
            entity.Property(e => e.Category).HasColumnName("category").HasMaxLength(50);
            entity.Property(e => e.Tags).HasColumnName("tags").HasColumnType("TEXT");
            entity.Property(e => e.LastAccessedAt).HasColumnName("last_accessed_at");
            entity.Property(e => e.AccessCount).HasColumnName("access_count").HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => new { e.UserId, e.LastAccessedAt })
                .HasDatabaseName("ix_memory_dynamics_user_accessed");
        });
    }

    private static void ConfigureMemoryAccessLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryAccessLog>(entity =>
        {
            entity.ToTable("memory_access_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MemoryId).HasColumnName("memory_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Grade).HasColumnName("grade");
            entity.Property(e => e.SignalType).HasColumnName("signal_type");
            entity.Property(e => e.RetrievabilityAtAccess).HasColumnName("retrievability_at_access");
            entity.Property(e => e.Context).HasColumnName("context").HasColumnType("TEXT");
            entity.Property(e => e.AccessedAt).HasColumnName("accessed_at");

            entity.HasIndex(e => e.MemoryId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.AccessedAt);
            entity.HasIndex(e => new { e.UserId, e.AccessedAt })
                .HasDatabaseName("ix_memory_access_user_time");

            entity.HasOne(e => e.Memory)
                .WithMany(m => m.AccessLogs)
                .HasForeignKey(e => e.MemoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureIntention(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Intention>(entity =>
        {
            entity.ToTable("intentions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id").HasDefaultValue("mypalclara");
            entity.Property(e => e.Content).HasColumnName("content").HasColumnType("TEXT");
            entity.Property(e => e.SourceMemoryId).HasColumnName("source_memory_id");
            entity.Property(e => e.TriggerConditions).HasColumnName("trigger_conditions").HasColumnType("TEXT");
            entity.Property(e => e.Priority).HasColumnName("priority").HasDefaultValue(0);
            entity.Property(e => e.Fired).HasColumnName("fired").HasDefaultValue(false);
            entity.Property(e => e.FireOnce).HasColumnName("fire_once").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.FiredAt).HasColumnName("fired_at");

            entity.HasIndex(e => new { e.UserId, e.Fired })
                .HasDatabaseName("ix_intention_user_unfired");
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("ix_intention_expires");
        });
    }

    private static void ConfigureMemorySupersession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemorySupersession>(entity =>
        {
            entity.ToTable("memory_supersessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OldMemoryId).HasColumnName("old_memory_id");
            entity.Property(e => e.NewMemoryId).HasColumnName("new_memory_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Confidence).HasColumnName("confidence").HasDefaultValue(1.0);
            entity.Property(e => e.Details).HasColumnName("details").HasColumnType("TEXT");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => e.OldMemoryId);
            entity.HasIndex(e => e.NewMemoryId);
            entity.HasIndex(e => e.UserId);
        });
    }

    private static void ConfigureMemoryHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryHistory>(entity =>
        {
            entity.ToTable("memory_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MemoryId).HasColumnName("memory_id");
            entity.Property(e => e.OldMemory).HasColumnName("old_memory").HasColumnType("TEXT");
            entity.Property(e => e.NewMemory).HasColumnName("new_memory").HasColumnType("TEXT");
            entity.Property(e => e.Event).HasColumnName("event");
            entity.Property(e => e.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            entity.Property(e => e.ActorId).HasColumnName("actor_id");
            entity.Property(e => e.Role).HasColumnName("role");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.MemoryId);
            entity.HasIndex(e => new { e.MemoryId, e.CreatedAt })
                .HasDatabaseName("ix_memory_history_memory_id_created");
        });
    }

    private static void ConfigureCanonicalUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CanonicalUser>(entity =>
        {
            entity.ToTable("canonical_users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.PrimaryEmail).HasColumnName("primary_email");
            entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("active");
            entity.Property(e => e.IsAdmin).HasColumnName("is_admin").HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.PrimaryEmail).IsUnique();
        });
    }

    private static void ConfigurePlatformLink(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlatformLink>(entity =>
        {
            entity.ToTable("platform_links");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CanonicalUserId).HasColumnName("canonical_user_id");
            entity.Property(e => e.Platform).HasColumnName("platform");
            entity.Property(e => e.PlatformUserId).HasColumnName("platform_user_id");
            entity.Property(e => e.PrefixedUserId).HasColumnName("prefixed_user_id");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.LinkedAt).HasColumnName("linked_at");
            entity.Property(e => e.LinkedVia).HasColumnName("linked_via");

            entity.HasIndex(e => e.PrefixedUserId).IsUnique();
            entity.HasIndex(e => new { e.Platform, e.PlatformUserId })
                .HasDatabaseName("ix_platform_link_platform_user")
                .IsUnique();

            entity.HasOne(e => e.CanonicalUser)
                .WithMany(u => u.PlatformLinks)
                .HasForeignKey(e => e.CanonicalUserId);
        });
    }

    private static void ConfigureOAuthToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OAuthToken>(entity =>
        {
            entity.ToTable("oauth_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CanonicalUserId).HasColumnName("canonical_user_id");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.AccessToken).HasColumnName("access_token").HasColumnType("TEXT");
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token").HasColumnType("TEXT");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.Scopes).HasColumnName("scopes").HasColumnType("TEXT");
            entity.Property(e => e.ProviderUserId).HasColumnName("provider_user_id");
            entity.Property(e => e.ProviderData).HasColumnName("provider_data").HasColumnType("TEXT");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => new { e.CanonicalUserId, e.Provider })
                .HasDatabaseName("ix_oauth_token_user_provider")
                .IsUnique();

            entity.HasOne(e => e.CanonicalUser)
                .WithMany(u => u.OAuthTokens)
                .HasForeignKey(e => e.CanonicalUserId);
        });
    }

    private static void ConfigureWebSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebSession>(entity =>
        {
            entity.ToTable("web_sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CanonicalUserId).HasColumnName("canonical_user_id");
            entity.Property(e => e.SessionTokenHash).HasColumnName("session_token_hash");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address");
            entity.Property(e => e.UserAgent).HasColumnName("user_agent");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(e => e.Revoked).HasColumnName("revoked").HasDefaultValue(false);

            entity.HasIndex(e => e.SessionTokenHash).IsUnique();

            entity.HasOne(e => e.CanonicalUser)
                .WithMany(u => u.WebSessions)
                .HasForeignKey(e => e.CanonicalUserId);
        });
    }

    private static void ConfigureToolAuditLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ToolAuditLog>(entity =>
        {
            entity.ToTable("tool_audit_logs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.Timestamp).HasColumnName("timestamp");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ToolName).HasColumnName("tool_name");
            entity.Property(e => e.Platform).HasColumnName("platform");
            entity.Property(e => e.Parameters).HasColumnName("parameters").HasColumnType("TEXT");
            entity.Property(e => e.ResultStatus).HasColumnName("result_status");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("TEXT");
            entity.Property(e => e.ExecutionTimeMs).HasColumnName("execution_time_ms");
            entity.Property(e => e.RiskLevel).HasColumnName("risk_level");
            entity.Property(e => e.Intent).HasColumnName("intent");
            entity.Property(e => e.ChannelId).HasColumnName("channel_id");

            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ToolName);
            entity.HasIndex(e => e.ChannelId);
            entity.HasIndex(e => new { e.UserId, e.Timestamp })
                .HasDatabaseName("ix_tool_audit_user_time");
            entity.HasIndex(e => new { e.ToolName, e.Timestamp })
                .HasDatabaseName("ix_tool_audit_tool_time");
        });
    }

    private static void ConfigurePersonalityTrait(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersonalityTrait>(entity =>
        {
            entity.ToTable("personality_traits");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id").HasDefaultValue("mypalclara");
            entity.Property(e => e.Category).HasColumnName("category").HasMaxLength(50);
            entity.Property(e => e.TraitKey).HasColumnName("trait_key").HasMaxLength(100);
            entity.Property(e => e.Content).HasColumnName("content").HasColumnType("TEXT");
            entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).HasDefaultValue("self");
            entity.Property(e => e.Reason).HasColumnName("reason").HasColumnType("TEXT");
            entity.Property(e => e.Active).HasColumnName("active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => new { e.AgentId, e.Category })
                .HasDatabaseName("ix_personality_trait_agent_category");
            entity.HasIndex(e => new { e.AgentId, e.Active })
                .HasDatabaseName("ix_personality_trait_agent_active");
        });
    }

    private static void ConfigurePersonalityTraitHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersonalityTraitHistory>(entity =>
        {
            entity.ToTable("personality_trait_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TraitId).HasColumnName("trait_id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.Event).HasColumnName("event").HasMaxLength(20);
            entity.Property(e => e.OldContent).HasColumnName("old_content").HasColumnType("TEXT");
            entity.Property(e => e.NewContent).HasColumnName("new_content").HasColumnType("TEXT");
            entity.Property(e => e.OldCategory).HasColumnName("old_category").HasMaxLength(50);
            entity.Property(e => e.NewCategory).HasColumnName("new_category").HasMaxLength(50);
            entity.Property(e => e.Reason).HasColumnName("reason").HasColumnType("TEXT");
            entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).HasDefaultValue("self");
            entity.Property(e => e.TriggerContext).HasColumnName("trigger_context").HasColumnType("TEXT");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => e.TraitId);
            entity.HasIndex(e => new { e.AgentId, e.CreatedAt })
                .HasDatabaseName("ix_personality_history_agent_created");

            entity.HasOne(e => e.Trait)
                .WithMany(t => t.History)
                .HasForeignKey(e => e.TraitId);
        });
    }

    private static void ConfigureMcpServer(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<McpServer>(entity =>
        {
            entity.ToTable("mcp_servers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ServerType).HasColumnName("server_type");
            entity.Property(e => e.SourceType).HasColumnName("source_type");
            entity.Property(e => e.SourceUrl).HasColumnName("source_url");
            entity.Property(e => e.ConfigPath).HasColumnName("config_path");
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("stopped");
            entity.Property(e => e.ToolCount).HasColumnName("tool_count").HasDefaultValue(0);
            entity.Property(e => e.LastError).HasColumnName("last_error").HasColumnType("TEXT");
            entity.Property(e => e.LastErrorAt).HasColumnName("last_error_at");
            entity.Property(e => e.OAuthRequired).HasColumnName("oauth_required").HasDefaultValue(false);
            entity.Property(e => e.OAuthTokenId).HasColumnName("oauth_token_id");
            entity.Property(e => e.TotalToolCalls).HasColumnName("total_tool_calls").HasDefaultValue(0);
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(e => e.InstalledBy).HasColumnName("installed_by");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => new { e.UserId, e.Name })
                .HasDatabaseName("ix_mcp_server_user_name");
            entity.HasIndex(e => e.Enabled)
                .HasDatabaseName("ix_mcp_server_enabled");

            entity.HasOne(e => e.OAuthToken)
                .WithMany(t => t.Servers)
                .HasForeignKey(e => e.OAuthTokenId);
        });
    }

    private static void ConfigureMcpOAuthToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<McpOAuthToken>(entity =>
        {
            entity.ToTable("mcp_oauth_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ServerName).HasColumnName("server_name");
            entity.Property(e => e.ServerUrl).HasColumnName("server_url");
            entity.Property(e => e.AuthorizationEndpoint).HasColumnName("authorization_endpoint");
            entity.Property(e => e.TokenEndpoint).HasColumnName("token_endpoint");
            entity.Property(e => e.RegistrationEndpoint).HasColumnName("registration_endpoint");
            entity.Property(e => e.ClientId).HasColumnName("client_id");
            entity.Property(e => e.ClientSecret).HasColumnName("client_secret").HasColumnType("TEXT");
            entity.Property(e => e.RedirectUri).HasColumnName("redirect_uri");
            entity.Property(e => e.CodeVerifier).HasColumnName("code_verifier");
            entity.Property(e => e.StateToken).HasColumnName("state_token");
            entity.Property(e => e.AccessToken).HasColumnName("access_token").HasColumnType("TEXT");
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token").HasColumnType("TEXT");
            entity.Property(e => e.TokenType).HasColumnName("token_type").HasDefaultValue("Bearer");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.Scopes).HasColumnName("scopes").HasColumnType("TEXT");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("pending");
            entity.Property(e => e.LastRefreshAt).HasColumnName("last_refresh_at");
            entity.Property(e => e.LastError).HasColumnName("last_error").HasColumnType("TEXT");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.ServerName })
                .HasDatabaseName("ix_mcp_oauth_user_server");
        });
    }

    private static void ConfigureMcpToolCall(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<McpToolCall>(entity =>
        {
            entity.ToTable("mcp_tool_calls");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.ServerId).HasColumnName("server_id");
            entity.Property(e => e.ServerName).HasColumnName("server_name");
            entity.Property(e => e.ToolName).HasColumnName("tool_name");
            entity.Property(e => e.Arguments).HasColumnName("arguments").HasColumnType("TEXT");
            entity.Property(e => e.ResultPreview).HasColumnName("result_preview").HasColumnType("TEXT");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.Success).HasColumnName("success").HasDefaultValue(true);
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("TEXT");
            entity.Property(e => e.ErrorType).HasColumnName("error_type");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.StartedAt)
                .HasDatabaseName("ix_mcp_tool_call_time");
            entity.HasIndex(e => new { e.ServerName, e.ToolName })
                .HasDatabaseName("ix_mcp_tool_call_server_tool");
            entity.HasIndex(e => new { e.UserId, e.StartedAt })
                .HasDatabaseName("ix_mcp_tool_call_user_time");

            entity.HasOne(e => e.Server)
                .WithMany(s => s.ToolCalls)
                .HasForeignKey(e => e.ServerId);
        });
    }

    private static void ConfigureMcpUsageMetrics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<McpUsageMetrics>(entity =>
        {
            entity.ToTable("mcp_usage_metrics");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ServerName).HasColumnName("server_name");
            entity.Property(e => e.Date).HasColumnName("date");
            entity.Property(e => e.CallCount).HasColumnName("call_count").HasDefaultValue(0);
            entity.Property(e => e.SuccessCount).HasColumnName("success_count").HasDefaultValue(0);
            entity.Property(e => e.ErrorCount).HasColumnName("error_count").HasDefaultValue(0);
            entity.Property(e => e.TimeoutCount).HasColumnName("timeout_count").HasDefaultValue(0);
            entity.Property(e => e.TotalDurationMs).HasColumnName("total_duration_ms").HasDefaultValue(0);
            entity.Property(e => e.AvgDurationMs).HasColumnName("avg_duration_ms").HasDefaultValue(0.0);
            entity.Property(e => e.ToolCounts).HasColumnName("tool_counts").HasColumnType("TEXT");
            entity.Property(e => e.FirstCallAt).HasColumnName("first_call_at");
            entity.Property(e => e.LastCallAt).HasColumnName("last_call_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => new { e.UserId, e.ServerName, e.Date })
                .HasDatabaseName("ix_mcp_metrics_unique")
                .IsUnique();
            entity.HasIndex(e => e.Date)
                .HasDatabaseName("ix_mcp_metrics_date");
        });
    }

    private static void ConfigureMcpRateLimit(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<McpRateLimit>(entity =>
        {
            entity.ToTable("mcp_rate_limits");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ServerName).HasColumnName("server_name");
            entity.Property(e => e.ToolName).HasColumnName("tool_name");
            entity.Property(e => e.MaxCallsPerMinute).HasColumnName("max_calls_per_minute");
            entity.Property(e => e.MaxCallsPerHour).HasColumnName("max_calls_per_hour");
            entity.Property(e => e.MaxCallsPerDay).HasColumnName("max_calls_per_day");
            entity.Property(e => e.CurrentMinuteCount).HasColumnName("current_minute_count").HasDefaultValue(0);
            entity.Property(e => e.CurrentHourCount).HasColumnName("current_hour_count").HasDefaultValue(0);
            entity.Property(e => e.CurrentDayCount).HasColumnName("current_day_count").HasDefaultValue(0);
            entity.Property(e => e.MinuteWindowStart).HasColumnName("minute_window_start");
            entity.Property(e => e.HourWindowStart).HasColumnName("hour_window_start");
            entity.Property(e => e.DayWindowStart).HasColumnName("day_window_start");
            entity.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.ServerName, e.ToolName })
                .HasDatabaseName("ix_mcp_rate_limit_scope");
        });
    }
}
