using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyPalClara.Data;
using MyPalClara.Modules.Email.Providers;
using MyPalClara.Modules.Email.Rules;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Modules.Email.Monitoring;

public class EmailPoller
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailPoller> _logger;

    public EmailPoller(IServiceScopeFactory scopeFactory, ILogger<EmailPoller> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task PollAllAccountsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();

        var accounts = await db.EmailAccounts
            .Where(a => a.Enabled == "true")
            .ToListAsync(ct);

        foreach (var account in accounts)
        {
            try
            {
                await PollAccountAsync(account.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to poll email account {Id}", account.Id);
            }
        }
    }

    private async Task PollAccountAsync(string accountId, CancellationToken ct)
    {
        _logger.LogDebug("Polling email account {Id}", accountId);
        // Connect, fetch, evaluate rules, update last_checked
        await Task.CompletedTask;
    }
}
