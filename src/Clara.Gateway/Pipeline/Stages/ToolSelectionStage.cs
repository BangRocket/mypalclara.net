using Clara.Core.Llm;
using Clara.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Clara.Gateway.Pipeline.Stages;

public class ToolSelectionStage
{
    private readonly ToolSelector _toolSelector;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<ToolSelectionStage> _logger;

    public ToolSelectionStage(
        ToolSelector toolSelector,
        IToolRegistry toolRegistry,
        ILogger<ToolSelectionStage> logger)
    {
        _toolSelector = toolSelector;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public virtual Task ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var categories = _toolSelector.SelectCategories(context.Content);

        var tools = new List<ToolDefinition>();
        foreach (var category in categories)
        {
            foreach (var tool in _toolRegistry.GetByCategory(category))
            {
                tools.Add(new ToolDefinition(tool.Name, tool.Description, tool.ParameterSchema));
            }
        }

        // Deduplicate by name
        context.SelectedTools = tools.DistinctBy(t => t.Name).ToList();

        _logger.LogDebug("Selected {Count} tools for message", context.SelectedTools.Count);

        return Task.CompletedTask;
    }
}
