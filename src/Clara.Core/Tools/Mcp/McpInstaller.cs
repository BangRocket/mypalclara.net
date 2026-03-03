using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Tools.Mcp;

/// <summary>
/// Installs MCP servers from various sources: Smithery, npm, or local paths.
/// </summary>
public partial class McpInstaller
{
    private readonly ILogger<McpInstaller> _logger;

    public McpInstaller(ILogger<McpInstaller>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<McpInstaller>.Instance;
    }

    /// <summary>
    /// Install an MCP server from a source string.
    /// Source formats:
    ///   "smithery:package-name"    -> smithery install package-name
    ///   "npm:@scope/package"       -> npx -y @scope/package
    ///   "npx:@scope/package"       -> npx -y @scope/package
    ///   "/path/to/server"          -> local path
    ///   "command args"             -> raw command
    /// </summary>
    public async Task<McpInstallResult> InstallAsync(string source, string? name = null, CancellationToken ct = default)
    {
        var (sourceType, package) = ParseSource(source);

        return sourceType switch
        {
            McpSourceType.Smithery => await InstallFromSmitheryAsync(package, name, ct),
            McpSourceType.Npm => InstallFromNpm(package, name),
            McpSourceType.Local => InstallFromLocal(package, name),
            McpSourceType.Raw => InstallRaw(source, name),
            _ => new McpInstallResult(false, name ?? "unknown", "", Error: $"Unknown source type for: {source}")
        };
    }

    /// <summary>
    /// Parse a source string to determine its type.
    /// </summary>
    internal static (McpSourceType Type, string Package) ParseSource(string source)
    {
        if (source.StartsWith("smithery:", StringComparison.OrdinalIgnoreCase))
            return (McpSourceType.Smithery, source["smithery:".Length..]);

        if (source.StartsWith("npm:", StringComparison.OrdinalIgnoreCase))
            return (McpSourceType.Npm, source["npm:".Length..]);

        if (source.StartsWith("npx:", StringComparison.OrdinalIgnoreCase))
            return (McpSourceType.Npm, source["npx:".Length..]);

        // Local path: starts with / or ./ or ../ or contains path separators
        if (source.StartsWith('/') || source.StartsWith("./") || source.StartsWith("../") ||
            (Path.IsPathRooted(source) && !source.Contains(' ')))
            return (McpSourceType.Local, source);

        // Scoped npm package pattern: @scope/package
        if (ScopedNpmPattern().IsMatch(source))
            return (McpSourceType.Npm, source);

        return (McpSourceType.Raw, source);
    }

    private async Task<McpInstallResult> InstallFromSmitheryAsync(string package, string? name, CancellationToken ct)
    {
        var serverName = name ?? SanitizeName(package);

        _logger.LogInformation("Installing MCP server from Smithery: {Package}", package);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "smithery",
                Arguments = $"install {package}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return new McpInstallResult(false, serverName, "", Error: "Failed to start smithery process");

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(ct);
                return new McpInstallResult(false, serverName, "", Error: $"Smithery install failed: {stderr}");
            }

            // After smithery install, the command is typically the package name
            var command = $"smithery run {package}";
            return new McpInstallResult(true, serverName, command);
        }
        catch (Exception ex)
        {
            return new McpInstallResult(false, serverName, "", Error: $"Smithery install error: {ex.Message}");
        }
    }

    private McpInstallResult InstallFromNpm(string package, string? name)
    {
        var serverName = name ?? SanitizeName(package);
        var command = "npx";
        var args = $"-y {package}";

        _logger.LogInformation("Registered MCP server from npm: {Package} (command: {Command} {Args})", package, command, args);

        return new McpInstallResult(true, serverName, command, Args: args);
    }

    private McpInstallResult InstallFromLocal(string path, string? name)
    {
        var serverName = name ?? Path.GetFileNameWithoutExtension(path);

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            _logger.LogWarning("Local MCP server path not found: {Path}", path);
            // Still register it — path might become valid later
        }

        _logger.LogInformation("Registered local MCP server: {Path}", path);

        return new McpInstallResult(true, serverName, path);
    }

    private McpInstallResult InstallRaw(string source, string? name)
    {
        // Split into command and args
        var parts = source.Split(' ', 2);
        var command = parts[0];
        var args = parts.Length > 1 ? parts[1] : null;
        var serverName = name ?? SanitizeName(command);

        _logger.LogInformation("Registered raw MCP server command: {Source}", source);

        return new McpInstallResult(true, serverName, command, Args: args);
    }

    private static string SanitizeName(string input)
    {
        // Remove @ prefix, replace / and special chars with _
        var sanitized = input.TrimStart('@').Replace('/', '_').Replace('\\', '_');
        return InvalidCharsPattern().Replace(sanitized, "").ToLowerInvariant();
    }

    [GeneratedRegex(@"^@[\w-]+/[\w.-]+$")]
    private static partial Regex ScopedNpmPattern();

    [GeneratedRegex(@"[^a-zA-Z0-9_-]")]
    private static partial Regex InvalidCharsPattern();
}

public enum McpSourceType
{
    Smithery,
    Npm,
    Local,
    Raw
}

public record McpInstallResult(
    bool Success,
    string ServerName,
    string Command,
    string? Args = null,
    string? Error = null);
