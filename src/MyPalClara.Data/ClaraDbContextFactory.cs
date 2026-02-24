using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Data;

public static class ClaraDbContextFactory
{
    private const string DefaultSqlitePath = "data/clara.db";

    /// <summary>
    /// Creates a ClaraDbContext from the DATABASE_URL environment variable (PostgreSQL)
    /// or falls back to a local SQLite database.
    /// </summary>
    public static ClaraDbContext Create(string? connectionString = null)
    {
        connectionString ??= Environment.GetEnvironmentVariable("DATABASE_URL");

        var optionsBuilder = new DbContextOptionsBuilder<ClaraDbContext>();

        if (!string.IsNullOrEmpty(connectionString) && IsPostgres(connectionString))
        {
            var npgsqlConnectionString = ConvertDatabaseUrl(connectionString);
            optionsBuilder.UseNpgsql(npgsqlConnectionString);
        }
        else
        {
            var sqlitePath = connectionString ?? $"Data Source={DefaultSqlitePath}";
            if (!sqlitePath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                sqlitePath = $"Data Source={sqlitePath}";
            optionsBuilder.UseSqlite(sqlitePath);
        }

        return new ClaraDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// Configures a DbContextOptionsBuilder from the DATABASE_URL environment variable
    /// or falls back to SQLite. Useful for dependency injection registration.
    /// </summary>
    public static void Configure(DbContextOptionsBuilder optionsBuilder, string? connectionString = null)
    {
        connectionString ??= Environment.GetEnvironmentVariable("DATABASE_URL");

        if (!string.IsNullOrEmpty(connectionString) && IsPostgres(connectionString))
        {
            var npgsqlConnectionString = ConvertDatabaseUrl(connectionString);
            optionsBuilder.UseNpgsql(npgsqlConnectionString);
        }
        else
        {
            var sqlitePath = connectionString ?? $"Data Source={DefaultSqlitePath}";
            if (!sqlitePath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                sqlitePath = $"Data Source={sqlitePath}";
            optionsBuilder.UseSqlite(sqlitePath);
        }
    }

    private static bool IsPostgres(string connectionString)
    {
        return connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts a PostgreSQL URL (postgresql://user:pass@host:port/db) to an
    /// Npgsql connection string (Host=host;Port=port;Database=db;Username=user;Password=pass).
    /// If the string is already in key=value format, returns it as-is.
    /// </summary>
    private static string ConvertDatabaseUrl(string url)
    {
        if (url.Contains("Host=", StringComparison.OrdinalIgnoreCase))
            return url;

        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':');
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');

        return $"Host={host};Port={port};Database={database};Username={username};Password={password}";
    }
}
