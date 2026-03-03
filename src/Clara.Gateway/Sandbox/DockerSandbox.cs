using System.Text;
using Clara.Core.Config;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clara.Gateway.Sandbox;

public class DockerSandbox : ISandboxProvider
{
    private readonly DockerClient _client;
    private readonly SandboxOptions _options;
    private readonly ILogger<DockerSandbox> _logger;

    private static readonly Dictionary<string, (string Image, string Command)> LanguageDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = ("python:3.12-slim", "python /code/main.py"),
        ["javascript"] = ("node:22-slim", "node /code/main.js"),
        ["bash"] = ("ubuntu:24.04", "bash /code/main.sh"),
    };

    public DockerSandbox(DockerClient client, IOptions<SandboxOptions> options, ILogger<DockerSandbox> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SandboxResult> ExecuteAsync(string code, string language, int timeoutSeconds = 30, CancellationToken ct = default)
    {
        var effectiveTimeout = Math.Min(timeoutSeconds, _options.Docker.TimeoutSeconds);
        var (image, runCommand) = ResolveLanguage(language);
        var extension = GetExtension(language);

        _logger.LogInformation("Executing {Language} code in Docker sandbox (timeout: {Timeout}s)", language, effectiveTimeout);

        // Create container
        var createResponse = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = image,
            Cmd = ["/bin/sh", "-c", $"cat > /code/main{extension} << 'CLARA_EOF'\n{code}\nCLARA_EOF\n{runCommand}"],
            WorkingDir = "/code",
            HostConfig = new HostConfig
            {
                Memory = ParseMemory(_options.Docker.Memory),
                NanoCPUs = (long)(_options.Docker.Cpu * 1_000_000_000),
                NetworkMode = "none",
                AutoRemove = false,
            },
        }, ct);

        var containerId = createResponse.ID;

        try
        {
            // Start container
            await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);

            // Wait for completion with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(effectiveTimeout));

            try
            {
                var waitResponse = await _client.Containers.WaitContainerAsync(containerId, cts.Token);
                var logs = await GetLogsAsync(containerId, ct);
                return new SandboxResult((int)waitResponse.StatusCode, logs.Stdout, logs.Stderr);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout
                await _client.Containers.KillContainerAsync(containerId, new ContainerKillParameters(), CancellationToken.None);
                return new SandboxResult(-1, "", $"Execution timed out after {effectiveTimeout} seconds");
            }
        }
        finally
        {
            // Clean up container
            try
            {
                await _client.Containers.RemoveContainerAsync(containerId,
                    new ContainerRemoveParameters { Force = true }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove container {ContainerId}", containerId);
            }
        }
    }

    private async Task<(string Stdout, string Stderr)> GetLogsAsync(string containerId, CancellationToken ct)
    {
        using var stream = await _client.Containers.GetContainerLogsAsync(containerId,
            false, new ContainerLogsParameters { ShowStdout = true, ShowStderr = true }, ct);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        // Docker.DotNet MultiplexedStream handles the demux header for us
        var buffer = new byte[8192];
        MultiplexedStream.ReadResult readResult;
        do
        {
            readResult = await stream.ReadOutputAsync(buffer, 0, buffer.Length, ct);
            if (readResult.Count > 0)
            {
                var text = Encoding.UTF8.GetString(buffer, 0, readResult.Count);
                if (readResult.Target == MultiplexedStream.TargetStream.StandardOut)
                    stdout.Append(text);
                else if (readResult.Target == MultiplexedStream.TargetStream.StandardError)
                    stderr.Append(text);
            }
        }
        while (!readResult.EOF);

        return (stdout.ToString(), stderr.ToString());
    }

    private (string Image, string Command) ResolveLanguage(string language)
    {
        if (LanguageDefaults.TryGetValue(language, out var defaults))
            return defaults;

        // Default to configured image with a generic approach
        return (_options.Docker.Image, $"sh /code/main.sh");
    }

    private static string GetExtension(string language) => language.ToLowerInvariant() switch
    {
        "python" => ".py",
        "javascript" or "js" => ".js",
        "bash" or "sh" => ".sh",
        "ruby" => ".rb",
        _ => ".sh",
    };

    private static long ParseMemory(string memory)
    {
        if (memory.EndsWith("g", StringComparison.OrdinalIgnoreCase))
            return long.Parse(memory[..^1]) * 1024 * 1024 * 1024;
        if (memory.EndsWith("m", StringComparison.OrdinalIgnoreCase))
            return long.Parse(memory[..^1]) * 1024 * 1024;
        return long.Parse(memory);
    }
}
