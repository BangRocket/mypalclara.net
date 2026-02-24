using Microsoft.Extensions.Logging;

namespace MyPalClara.Modules.Mcp.Auth;

public class OAuthManager
{
    private readonly ILogger<OAuthManager> _logger;

    public OAuthManager(ILogger<OAuthManager> logger) => _logger = logger;

    public Task<string> StartFlowAsync(string serverName, CancellationToken ct = default)
        => Task.FromResult($"https://auth.example.com/authorize?server={serverName}");

    public Task CompleteFlowAsync(string serverName, string code, CancellationToken ct = default)
    {
        _logger.LogInformation("OAuth completed for {Server} with code {Code}", serverName, code);
        return Task.CompletedTask;
    }

    public Task<string> GetStatusAsync(string serverName, CancellationToken ct = default)
        => Task.FromResult("none");
}
