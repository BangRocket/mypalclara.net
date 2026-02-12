using System.Security.Cryptography;
using System.Text;
using FxSsh;
using FxSsh.Services;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Ssh;

/// <summary>
/// SSH server that accepts connections and spawns a Clara REPL session per connection.
/// Uses FxSsh as the SSH server library.
/// </summary>
public sealed class SshServerHost : IDisposable
{
    private readonly ClaraConfig _config;
    private readonly Func<GatewayClient> _gatewayClientFactory;
    private readonly ILogger<SshServerHost> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private SshServer? _server;

    public SshServerHost(
        ClaraConfig config,
        Func<GatewayClient> gatewayClientFactory,
        ILoggerFactory loggerFactory)
    {
        _config = config;
        _gatewayClientFactory = gatewayClientFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SshServerHost>();
    }

    public void Start()
    {
        var port = _config.Ssh?.Port ?? 2222;
        var info = new StartingInfo(System.Net.IPAddress.Any, port, "SSH-2.0-MyPalClara");

        _server = new SshServer(info);

        // Generate an ephemeral RSA host key if none configured
        var hostKey = _config.Ssh?.HostKey;
        if (string.IsNullOrEmpty(hostKey))
        {
            using var rsa = RSA.Create(2048);
            hostKey = rsa.ToXmlString(true);
            _logger.LogInformation("Generated ephemeral RSA host key (configure Ssh:HostKey for persistence)");
        }

        _server.AddHostKey("rsa-sha2-256", hostKey);
        _server.ConnectionAccepted += OnConnectionAccepted;
        _server.ExceptionRasied += (_, ex) => _logger.LogWarning(ex, "SSH server exception");
        _server.Start();

        _logger.LogInformation("SSH server listening on port {Port}", port);
    }

    public void Stop()
    {
        _server?.Stop();
        _logger.LogInformation("SSH server stopped");
    }

    private void OnConnectionAccepted(object? sender, Session session)
    {
        _logger.LogDebug("SSH connection accepted from {Client}", session.ClientVersion);

        session.ServiceRegistered += (_, service) =>
        {
            if (service is UserauthService auth)
            {
                auth.Userauth += OnUserAuth;
            }
            else if (service is ConnectionService conn)
            {
                conn.CommandOpened += OnCommandOpened;
            }
        };
    }

    private void OnUserAuth(object? sender, UserauthArgs args)
    {
        // Accept all connections for now — authentication is handled by the Gateway secret
        // TODO: Add password/key auth against config if needed
        _logger.LogDebug("SSH auth attempt: user={User}, method={Method}", args.Username, args.AuthMethod);
        args.Result = true;
    }

    private void OnCommandOpened(object? sender, CommandRequestedArgs args)
    {
        if (args.ShellType is not ("shell" or "exec"))
            return;

        _logger.LogInformation("SSH session opened: type={Type}, user={User}",
            args.ShellType, args.AttachedUserauthArgs.Username);

        var username = args.AttachedUserauthArgs.Username;
        var channel = args.Channel;

        // Launch the REPL session on a background thread
        _ = Task.Run(async () =>
        {
            var sessionLogger = _loggerFactory.CreateLogger<SshSession>();
            var gateway = _gatewayClientFactory();

            try
            {
                await gateway.ConnectAsync();
                var sshSession = new SshSession(gateway, _config, channel, username, sessionLogger);
                await sshSession.RunAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSH session failed for user {User}", username);
                try
                {
                    var errorBytes = Encoding.UTF8.GetBytes($"\r\nError: {ex.Message}\r\n");
                    channel.SendData(errorBytes);
                }
                catch { /* best effort */ }
            }
            finally
            {
                await gateway.DisposeAsync();
                channel.SendClose(0);
            }
        });
    }

    public void Dispose()
    {
        _server?.Dispose();
    }
}
