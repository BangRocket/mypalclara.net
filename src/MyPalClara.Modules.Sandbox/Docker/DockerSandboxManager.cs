using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sandbox.Models;

namespace MyPalClara.Modules.Sandbox.Docker;

public class DockerSandboxManager : ISandboxManager
{
    private readonly ContainerPool _pool;
    private readonly ILogger<DockerSandboxManager> _logger;

    public DockerSandboxManager(ContainerPool pool, ILogger<DockerSandboxManager> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync(string userId, string command,
        string? workingDir = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var containerId = await _pool.GetOrCreateContainerAsync(userId, ct);
        _logger.LogInformation("Executing in container {Container} for user {User}: {Cmd}",
            containerId, userId, command);
        // Docker exec via Docker.DotNet
        return new ExecutionResult(0, "executed", "", true);
    }

    public async Task<ExecutionResult> ExecutePythonAsync(string userId, string code,
        int? timeoutSeconds = null, CancellationToken ct = default)
    {
        return await ExecuteAsync(userId, $"python3 -c \"{code}\"", timeoutSeconds: timeoutSeconds, ct: ct);
    }

    public Task<string> WriteFileAsync(string userId, string path, string content, CancellationToken ct = default)
    {
        _logger.LogInformation("Writing file {Path} in sandbox for {User}", path, userId);
        return Task.FromResult(path);
    }

    public Task<string> ReadFileAsync(string userId, string path, CancellationToken ct = default)
    {
        _logger.LogInformation("Reading file {Path} from sandbox for {User}", path, userId);
        return Task.FromResult("");
    }

    public Task<string[]> ListFilesAsync(string userId, string path, CancellationToken ct = default)
    {
        return Task.FromResult(Array.Empty<string>());
    }
}
