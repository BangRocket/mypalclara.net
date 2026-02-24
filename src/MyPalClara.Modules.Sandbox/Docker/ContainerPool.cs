using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Modules.Sandbox.Docker;

public class ContainerPool
{
    private readonly ConcurrentDictionary<string, (string ContainerId, DateTime LastUsed)> _containers = new();
    private readonly ILogger<ContainerPool> _logger;
    private readonly string _image;

    public ContainerPool(ILogger<ContainerPool> logger)
    {
        _logger = logger;
        _image = Environment.GetEnvironmentVariable("DOCKER_SANDBOX_IMAGE") ?? "python:3.12-slim";
    }

    public async Task<string> GetOrCreateContainerAsync(string userId, CancellationToken ct = default)
    {
        if (_containers.TryGetValue(userId, out var existing))
        {
            _containers[userId] = existing with { LastUsed = DateTime.UtcNow };
            return existing.ContainerId;
        }

        var containerId = $"clara-sandbox-{userId}-{Guid.NewGuid():N}";
        _containers[userId] = (containerId, DateTime.UtcNow);
        _logger.LogInformation("Created sandbox container {Id} from {Image} for {User}",
            containerId, _image, userId);

        return containerId;
    }

    public async Task CleanupIdleAsync(TimeSpan maxIdle, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxIdle;
        foreach (var (userId, (containerId, lastUsed)) in _containers)
        {
            if (lastUsed < cutoff)
            {
                _containers.TryRemove(userId, out _);
                _logger.LogInformation("Removed idle container {Id}", containerId);
            }
        }
    }
}
