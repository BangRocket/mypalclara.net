using MyPalClara.Core.Llm;
using MyPalClara.Agent.Mcp;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Agent.Orchestration;

/// <summary>Routes tool calls from the LLM to the appropriate MCP server.</summary>
public sealed class ToolExecutor
{
    private readonly McpServerManager _mcpManager;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(McpServerManager mcpManager, ILogger<ToolExecutor> logger)
    {
        _mcpManager = mcpManager;
        _logger = logger;
    }

    /// <summary>Execute a tool call and return the result text.</summary>
    public async Task<string> ExecuteAsync(ToolCall toolCall, CancellationToken ct = default)
    {
        _logger.LogDebug("Executing tool: {Tool}", toolCall.Name);

        try
        {
            var result = await _mcpManager.CallToolAsync(toolCall.Name, toolCall.Arguments, ct);
            _logger.LogDebug("Tool {Tool} returned {Len} chars", toolCall.Name, result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {Tool}", toolCall.Name);
            return $"Error: Tool '{toolCall.Name}' failed: {ex.Message}";
        }
    }
}
