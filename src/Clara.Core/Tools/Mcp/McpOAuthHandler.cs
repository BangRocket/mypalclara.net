using System.Collections.Concurrent;

namespace Clara.Core.Tools.Mcp;

/// <summary>
/// Manages OAuth tokens for hosted MCP servers that require authentication.
/// Stores tokens in memory with expiration tracking and supports refresh.
/// </summary>
public class McpOAuthHandler
{
    private readonly ConcurrentDictionary<string, OAuthToken> _tokens = new();

    /// <summary>
    /// Get a valid access token for a server, or null if none exists or all are expired.
    /// </summary>
    public Task<string?> GetTokenAsync(string serverName, CancellationToken ct = default)
    {
        if (!_tokens.TryGetValue(serverName, out var token))
            return Task.FromResult<string?>(null);

        // Check expiration with a 60-second buffer
        if (token.ExpiresAt <= DateTime.UtcNow.AddSeconds(60))
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(token.AccessToken);
    }

    /// <summary>
    /// Store an OAuth token for a server.
    /// </summary>
    public Task StoreTokenAsync(
        string serverName,
        string accessToken,
        string refreshToken,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        _tokens[serverName] = new OAuthToken(accessToken, refreshToken, expiresAt);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get the refresh token for a server, for use in token refresh flows.
    /// </summary>
    public Task<string?> GetRefreshTokenAsync(string serverName, CancellationToken ct = default)
    {
        if (!_tokens.TryGetValue(serverName, out var token))
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(token.RefreshToken);
    }

    /// <summary>
    /// Remove stored tokens for a server.
    /// </summary>
    public Task RevokeTokenAsync(string serverName, CancellationToken ct = default)
    {
        _tokens.TryRemove(serverName, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if a server has a valid (non-expired) token.
    /// </summary>
    public bool HasValidToken(string serverName)
    {
        return _tokens.TryGetValue(serverName, out var token)
            && token.ExpiresAt > DateTime.UtcNow.AddSeconds(60);
    }

    private record OAuthToken(string AccessToken, string RefreshToken, DateTime ExpiresAt);
}
