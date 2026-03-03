using Clara.Core.Data;
using Clara.Core.Events;
using Clara.Core.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clara.Gateway.Services;

public class SessionCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IClaraEventBus _eventBus;
    private readonly ILogger<SessionCleanupService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _idleTimeout = TimeSpan.FromHours(2);

    public SessionCleanupService(
        IServiceProvider services,
        IClaraEventBus eventBus,
        ILogger<SessionCleanupService> logger)
    {
        _services = services;
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Session cleanup service started (interval: {Interval}, timeout: {Timeout})",
            _checkInterval, _idleTimeout);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_checkInterval, stoppingToken);

            try
            {
                await CleanupIdleSessionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session cleanup");
            }
        }
    }

    private async Task CleanupIdleSessionsAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();
        var sessionManager = scope.ServiceProvider.GetRequiredService<ISessionManager>();

        var cutoff = DateTime.UtcNow - _idleTimeout;

        var idleSessions = await db.Sessions
            .Where(s => s.Status == "active" && s.UpdatedAt < cutoff)
            .Select(s => s.SessionKey)
            .ToListAsync(ct);

        if (idleSessions.Count == 0) return;

        _logger.LogInformation("Found {Count} idle sessions to clean up", idleSessions.Count);

        foreach (var key in idleSessions)
        {
            await sessionManager.TimeoutAsync(key, ct);

            await _eventBus.PublishAsync(new ClaraEvent(SessionEvents.Timeout, DateTime.UtcNow,
                new Dictionary<string, object> { ["sessionKey"] = key })
            {
                SessionKey = key,
            });
        }

        _logger.LogInformation("Cleaned up {Count} idle sessions", idleSessions.Count);
    }
}
