using Microsoft.Extensions.Logging;

namespace Clara.Gateway.Pipeline.Middleware;

public class StopPhraseMiddleware : IPipelineMiddleware
{
    private static readonly string[] StopPhrases =
    [
        "clara stop",
        "nevermind",
        "never mind",
        "cancel",
        "stop",
    ];

    private readonly ILogger<StopPhraseMiddleware> _logger;

    public StopPhraseMiddleware(ILogger<StopPhraseMiddleware> logger)
    {
        _logger = logger;
    }

    public int Order => 0;

    public Task InvokeAsync(PipelineContext context, CancellationToken ct = default)
    {
        var trimmed = context.Content.Trim();

        foreach (var phrase in StopPhrases)
        {
            if (trimmed.Equals(phrase, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Stop phrase detected: \"{Phrase}\" for session {SessionKey}",
                    phrase, context.SessionKey);
                context.Cancelled = true;
                return Task.CompletedTask;
            }
        }

        return Task.CompletedTask;
    }
}
