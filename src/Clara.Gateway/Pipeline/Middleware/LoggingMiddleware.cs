using Microsoft.Extensions.Logging;

namespace Clara.Gateway.Pipeline.Middleware;

public class LoggingMiddleware : IPipelineMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }

    public int Order => -10; // Run first

    public Task InvokeAsync(PipelineContext context, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Message from {UserId} on {Platform} in session {SessionKey}: {ContentLength} chars",
            context.UserId, context.Platform, context.SessionKey, context.Content.Length);

        return Task.CompletedTask;
    }
}
