using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Email.Models;

namespace MyPalClara.Modules.Email.Providers;

public class ImapProvider : IEmailProvider
{
    private readonly string _server;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly ILogger _logger;

    public ImapProvider(string server, int port, string username, string password, ILogger logger)
    {
        _server = server;
        _port = port;
        _username = username;
        _password = password;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // MailKit: connect to IMAP server
        _logger.LogDebug("Connecting to IMAP {Server}:{Port}", _server, _port);
        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<EmailMessage>> FetchUnreadAsync(string? sinceUid = null,
        CancellationToken ct = default)
    {
        // MailKit: fetch unread messages
        await Task.CompletedTask;
        return [];
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Disconnecting from IMAP {Server}", _server);
        await Task.CompletedTask;
    }
}
