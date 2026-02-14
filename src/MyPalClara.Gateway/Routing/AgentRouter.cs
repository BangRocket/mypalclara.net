using MyPalClara.Core.Configuration;

namespace MyPalClara.Gateway.Routing;

/// <summary>
/// Resolves an agent profile for a given adapter/channel combination.
/// Matches "{adapterType}-{channelId}" against ChannelRouting glob patterns.
/// First match wins; "*" is wildcard default.
/// </summary>
public sealed class AgentRouter
{
    private readonly ClaraConfig _config;

    public AgentRouter(ClaraConfig config)
    {
        _config = config;
    }

    public AgentProfile? ResolveProfile(string adapterType, string channelId)
    {
        if (!_config.Agents.MultiAgentEnabled)
            return null;

        var key = $"{adapterType}-{channelId}";

        foreach (var (pattern, profileName) in _config.Agents.ChannelRouting)
        {
            if (MatchesPattern(key, pattern))
            {
                return _config.Agents.Profiles.GetValueOrDefault(profileName);
            }
        }

        return null;
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        if (pattern == "*")
            return true;

        if (pattern.EndsWith('*'))
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);

        return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
