using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Clara.Core.Configuration;

/// <summary>
/// Loads <see cref="ClaraConfig"/> from a YAML file with environment variable overlay.
/// Env vars use __ as nested delimiter (e.g. LLM__ANTHROPIC__API_KEY).
/// </summary>
public static class ConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Resolves the path to clara.yaml:
    /// 1. Explicit <paramref name="explicitPath"/> (--config CLI arg)
    /// 2. CLARA_YAML_PATH env var
    /// 3. Default: ../mypalclara/clara.yaml relative to CWD
    /// </summary>
    public static string ResolveConfigPath(string? explicitPath = null)
    {
        if (!string.IsNullOrEmpty(explicitPath))
            return Path.GetFullPath(explicitPath);

        var envPath = Environment.GetEnvironmentVariable("CLARA_YAML_PATH");
        if (!string.IsNullOrEmpty(envPath))
            return Path.GetFullPath(envPath);

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "mypalclara", "clara.yaml"));
    }

    /// <summary>Load and return <see cref="ClaraConfig"/> from the given YAML path.</summary>
    public static ClaraConfig Load(string yamlPath)
    {
        if (!File.Exists(yamlPath))
            throw new FileNotFoundException($"Config file not found: {yamlPath}");

        var yaml = File.ReadAllText(yamlPath);
        var config = Deserializer.Deserialize<ClaraConfig>(yaml) ?? new ClaraConfig();

        // Resolve relative paths against the directory containing clara.yaml
        var configDir = Path.GetDirectoryName(Path.GetFullPath(yamlPath))!;
        config.DataDir = ResolvePath(configDir, config.DataDir);
        config.FilesDir = ResolvePath(configDir, config.FilesDir);

        if (!string.IsNullOrEmpty(config.Bot.PersonalityFile))
            config.Bot.PersonalityFile = ResolvePath(configDir, config.Bot.PersonalityFile);

        if (!string.IsNullOrEmpty(config.Mcp.ServersDir))
            config.Mcp.ServersDir = ResolvePath(configDir, config.Mcp.ServersDir);

        // Apply environment variable overrides (LLM__PROVIDER=openai → config.Llm.Provider)
        ApplyEnvironmentOverrides(config);

        // Normalize PostgreSQL URIs (postgresql://...) to Npgsql key-value connection strings
        config.Database.Url = NormalizePostgresUrl(config.Database.Url);
        config.Memory.VectorStore.DatabaseUrl = NormalizePostgresUrl(config.Memory.VectorStore.DatabaseUrl);

        // Normalize Redis URIs (redis://...) to StackExchange.Redis configuration strings
        config.Memory.RedisUrl = NormalizeRedisUrl(config.Memory.RedisUrl);

        return config;
    }

    private static string ResolvePath(string baseDir, string path)
    {
        if (Path.IsPathRooted(path)) return path;
        return Path.GetFullPath(Path.Combine(baseDir, path));
    }

    /// <summary>
    /// Scans environment variables for CLARA_ or direct section overrides using __ as nested delimiter.
    /// E.g. LLM__PROVIDER=openai sets config.Llm.Provider = "openai".
    /// </summary>
    private static void ApplyEnvironmentOverrides(ClaraConfig config)
    {
        // Map of top-level YAML keys to config properties
        var sectionMap = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["llm"] = config.Llm,
            ["memory"] = config.Memory,
            ["database"] = config.Database,
            ["gateway"] = config.Gateway,
            ["bot"] = config.Bot,
            ["mcp"] = config.Mcp,
        };

        foreach (var envVar in Environment.GetEnvironmentVariables().Keys.Cast<string>())
        {
            var parts = envVar.Split("__");
            if (parts.Length < 2) continue;

            var sectionKey = parts[0].ToLowerInvariant();
            if (!sectionMap.TryGetValue(sectionKey, out var section))
                continue;

            var value = Environment.GetEnvironmentVariable(envVar);
            if (value is null) continue;

            SetNestedProperty(section, parts.AsSpan(1), value);
        }
    }

    private static void SetNestedProperty(object target, ReadOnlySpan<string> path, string value)
    {
        for (int i = 0; i < path.Length; i++)
        {
            var propName = path[i];
            var prop = FindProperty(target.GetType(), propName);
            if (prop is null) return;

            if (i == path.Length - 1)
            {
                // Terminal — set the value
                var converted = ConvertValue(value, prop.PropertyType);
                if (converted is not null)
                    prop.SetValue(target, converted);
            }
            else
            {
                // Intermediate — navigate deeper
                var next = prop.GetValue(target);
                if (next is null) return;
                target = next;
            }
        }
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        // Try exact PascalCase match, then case-insensitive
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                   .FirstOrDefault(p => MatchesSnakeCase(p.Name, name));
    }

    private static bool MatchesSnakeCase(string pascalName, string snakeName)
    {
        // Convert PascalCase to snake_case for comparison
        var converted = string.Concat(
            pascalName.Select((c, i) =>
                i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
        return string.Equals(converted, snakeName.ToLowerInvariant(), StringComparison.Ordinal);
    }

    private static object? ConvertValue(string value, Type targetType)
    {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(int) && int.TryParse(value, out var i)) return i;
        if (targetType == typeof(bool) && bool.TryParse(value, out var b)) return b;
        if (targetType == typeof(float) && float.TryParse(value, out var f)) return f;
        if (targetType == typeof(double) && double.TryParse(value, out var d)) return d;
        return null;
    }

    /// <summary>
    /// Converts a PostgreSQL URI (postgres:// or postgresql://) to an Npgsql key-value connection string.
    /// Npgsql does not accept URI-format strings; it requires Host=...;Database=...;Username=... format.
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

        // Map URI query parameters (e.g. ?sslmode=require)
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
    /// StackExchange.Redis expects "host:port,password=...,defaultDatabase=..." format.
    /// </summary>
    internal static string NormalizeRedisUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;

        // Already in config format (contains comma-separated options or no scheme)
        if (!url.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
            return url;

        var tls = url.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 6379;
        var parts = new List<string> { $"{host}:{port}" };

        // Password from userinfo (redis://:password@host or redis://user:password@host)
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var creds = uri.UserInfo.Split(':', 2);
            var password = creds.Length > 1
                ? Uri.UnescapeDataString(creds[1])
                : Uri.UnescapeDataString(creds[0]);
            if (!string.IsNullOrEmpty(password))
                parts.Add($"password={password}");
        }

        // Database number from path (redis://host:port/0)
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
