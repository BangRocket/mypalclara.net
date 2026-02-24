using Microsoft.Extensions.Logging;

namespace MyPalClara.Modules.Mcp.Install;

public class McpInstaller
{
    private readonly ILogger<McpInstaller> _logger;
    private readonly SmitheryClient _smithery;

    public McpInstaller(ILogger<McpInstaller> logger, SmitheryClient smithery)
    {
        _logger = logger;
        _smithery = smithery;
    }

    public async Task<string> InstallNpmAsync(string package, string? name = null, CancellationToken ct = default)
    {
        name ??= package.Split('/').Last().Replace("@", "").Replace("-", "_");
        _logger.LogInformation("Installing npm MCP server: {Package} as {Name}", package, name);
        // npx -y {package} -- would spawn and capture config output
        return name;
    }

    public async Task<string> InstallSmitheryAsync(string url, string? name = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Installing Smithery MCP server from {Url}", url);
        return name ?? "smithery_server";
    }
}
