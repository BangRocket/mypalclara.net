using Clara.Core.Config;
using Clara.Core.Events;
using Clara.Core.Llm;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clara.Gateway.Services;

public class HeartbeatService : BackgroundService
{
    private readonly HeartbeatOptions _options;
    private readonly ILlmProviderFactory _providerFactory;
    private readonly IClaraEventBus _eventBus;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(
        IOptions<HeartbeatOptions> options,
        ILlmProviderFactory providerFactory,
        IClaraEventBus eventBus,
        ILogger<HeartbeatService> logger)
    {
        _options = options.Value;
        _providerFactory = providerFactory;
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Heartbeat service disabled");
            return;
        }

        _logger.LogInformation("Heartbeat service started (interval: {Interval}m, checklist: {Path})",
            _options.IntervalMinutes, _options.ChecklistPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateChecklistAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Heartbeat evaluation failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
        }
    }

    private async Task EvaluateChecklistAsync(CancellationToken ct)
    {
        var checklistPath = _options.ChecklistPath;
        if (!File.Exists(checklistPath))
        {
            _logger.LogDebug("No heartbeat checklist at {Path}", checklistPath);
            return;
        }

        var content = await File.ReadAllTextAsync(checklistPath, ct);
        var items = ParseChecklistItems(content);

        if (items.Count == 0)
        {
            _logger.LogDebug("Heartbeat: no checklist items found");
            return;
        }

        var provider = _providerFactory.GetProvider();
        var model = _providerFactory.ResolveModel(provider.Name, ModelTier.Low);

        foreach (var item in items)
        {
            var request = new LlmRequest(
                model,
                [
                    LlmMessage.System("You evaluate checklist items. Respond with ONLY 'ACTION: <brief description>' if action is needed, or 'OK' if not. Current time: " + DateTime.UtcNow.ToString("u")),
                    LlmMessage.User($"Evaluate this checklist item: {item}")
                ],
                Temperature: 0.1f);

            var response = await provider.CompleteAsync(request, ct);
            var text = string.Join("", response.Content.OfType<TextContent>().Select(c => c.Text));

            if (text.StartsWith("ACTION:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Heartbeat action needed: {Item} -> {Action}", item, text);
                await _eventBus.PublishAsync(new ClaraEvent(HeartbeatEvents.Action, DateTime.UtcNow,
                    new Dictionary<string, object>
                    {
                        ["item"] = item,
                        ["action"] = text
                    }));
            }
            else
            {
                _logger.LogDebug("Heartbeat OK: {Item}", item);
            }
        }
    }

    public static List<string> ParseChecklistItems(string markdown)
    {
        return markdown.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- [ ]") || line.StartsWith("- [x]"))
            .Select(line => line.Length > 5 ? line[5..].Trim() : "")
            .Where(item => !string.IsNullOrEmpty(item))
            .ToList();
    }
}
