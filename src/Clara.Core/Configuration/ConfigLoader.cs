using Microsoft.Extensions.Configuration;

namespace Clara.Core.Configuration;

/// <summary>
/// Binds <see cref="ClaraConfig"/> from the host's <see cref="IConfiguration"/>
/// (appsettings.json + env vars) and normalizes connection strings.
/// </summary>
public static class ConfigLoader
{
    /// <summary>Bind <see cref="ClaraConfig"/> from the host configuration and normalize URIs.</summary>
    public static ClaraConfig Bind(IConfiguration configuration)
    {
        var config = new ClaraConfig();
        configuration.Bind(config);

        // Resolve relative paths against CWD
        var baseDir = Directory.GetCurrentDirectory();
        config.DataDir = ResolvePath(baseDir, config.DataDir);
        config.FilesDir = ResolvePath(baseDir, config.FilesDir);

        if (!string.IsNullOrEmpty(config.Bot.PersonalityFile))
            config.Bot.PersonalityFile = ResolvePath(baseDir, config.Bot.PersonalityFile);

        if (!string.IsNullOrEmpty(config.Mcp.ServersDir))
            config.Mcp.ServersDir = ResolvePath(baseDir, config.Mcp.ServersDir);

        // Normalize PostgreSQL URIs (postgresql://...) to Npgsql key-value connection strings
        config.Database.Url = NormalizePostgresUrl(config.Database.Url);
        config.Memory.VectorStore.DatabaseUrl = NormalizePostgresUrl(config.Memory.VectorStore.DatabaseUrl);

        // Normalize Redis URIs (redis://...) to StackExchange.Redis configuration strings
        config.Memory.RedisUrl = NormalizeRedisUrl(config.Memory.RedisUrl);

        return config;
    }

    private static string ResolvePath(string baseDir, string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (Path.IsPathRooted(path)) return path;
        return Path.GetFullPath(Path.Combine(baseDir, path));
    }

    /// <summary>
    /// Converts a PostgreSQL URI (postgres:// or postgresql://) to an Npgsql key-value connection string.
    /// </summary>
    internal static string NormalizePostgresUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;

        // Already in key-value format
        if (url.Contains('=')) return url;

        if (!url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return url;

        // Normalize postgres:// → postgresql:// so Uri can parse the scheme
        var normalized = url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            ? "postgresql://" + url["postgres://".Length..]
            : url;

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return url;

        var parts = new List<string> { $"Host={uri.Host}" };

        if (uri.Port > 0)
            parts.Add($"Port={uri.Port}");

        if (uri.AbsolutePath.Length > 1)
            parts.Add($"Database={uri.AbsolutePath.TrimStart('/')}");

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var creds = uri.UserInfo.Split(':', 2);
            parts.Add($"Username={Uri.UnescapeDataString(creds[0])}");
            if (creds.Length > 1)
                parts.Add($"Password={Uri.UnescapeDataString(creds[1])}");
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            foreach (var param in uri.Query.TrimStart('?').Split('&'))
            {
                var kv = param.Split('=', 2);
                if (kv.Length == 2)
                {
                    var key = Uri.UnescapeDataString(kv[0]) switch
                    {
                        "sslmode" => "SSL Mode",
                        var k => k
                    };
                    parts.Add($"{key}={Uri.UnescapeDataString(kv[1])}");
                }
            }
        }

        return string.Join(";", parts);
    }

    /// <summary>
    /// Converts a Redis URI (redis:// or rediss://) to a StackExchange.Redis configuration string.
    /// </summary>
    internal static string NormalizeRedisUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;

        if (!url.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
            return url;

        var tls = url.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 6379;
        var parts = new List<string> { $"{host}:{port}" };

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var creds = uri.UserInfo.Split(':', 2);
            var password = creds.Length > 1
                ? Uri.UnescapeDataString(creds[1])
                : Uri.UnescapeDataString(creds[0]);
            if (!string.IsNullOrEmpty(password))
                parts.Add($"password={password}");
        }

        if (uri.AbsolutePath.Length > 1)
        {
            var db = uri.AbsolutePath.TrimStart('/');
            if (int.TryParse(db, out _))
                parts.Add($"defaultDatabase={db}");
        }

        if (tls)
            parts.Add("ssl=true");

        return string.Join(",", parts);
    }
}
