using Microsoft.Extensions.Logging;

namespace MyPalClara.Modules.Sandbox.Docker;

/// <summary>
/// HTTP client for Docker Engine API via Unix socket (/var/run/docker.sock).
/// </summary>
public class DockerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DockerClient> _logger;

    public DockerClient(ILogger<DockerClient> logger)
    {
        _logger = logger;
        var socketPath = Environment.GetEnvironmentVariable("DOCKER_HOST") ?? "unix:///var/run/docker.sock";
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, ct) =>
            {
                var socket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.Unix,
                    System.Net.Sockets.SocketType.Stream,
                    System.Net.Sockets.ProtocolType.Unspecified);
                await socket.ConnectAsync(
                    new System.Net.Sockets.UnixDomainSocketEndPoint("/var/run/docker.sock"), ct);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }

    public async Task<string> CreateContainerAsync(string image, string name, CancellationToken ct = default)
    {
        _logger.LogDebug("Creating container {Name} from {Image}", name, image);
        return name;
    }

    public async Task StartContainerAsync(string containerId, CancellationToken ct = default)
    {
        _logger.LogDebug("Starting container {Id}", containerId);
    }

    public async Task<(string Stdout, string Stderr, int ExitCode)> ExecAsync(
        string containerId, string[] command, int timeoutSeconds = 900, CancellationToken ct = default)
    {
        _logger.LogDebug("Exec in {Id}: {Cmd}", containerId, string.Join(" ", command));
        return ("", "", 0);
    }
}
