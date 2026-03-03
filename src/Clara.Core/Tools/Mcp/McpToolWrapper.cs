using System.Text.Json;

namespace Clara.Core.Tools.Mcp;

/// <summary>
/// Wraps an MCP server tool as an ITool, delegating execution to McpServerManager.
/// Tool names are namespaced as "mcp_{server}_{tool}" to avoid collisions.
/// </summary>
public class McpToolWrapper : ITool
{
    private readonly McpServerManager _serverManager;
    private readonly string _serverName;
    private readonly string _originalToolName;

    public McpToolWrapper(
        McpServerManager serverManager,
        string serverName,
        McpToolInfo toolInfo)
    {
        _serverManager = serverManager;
        _serverName = serverName;
        _originalToolName = toolInfo.Name;
        Name = $"mcp_{serverName}_{toolInfo.Name}";
        Description = toolInfo.Description;
        ParameterSchema = JsonDocument.Parse(toolInfo.ParameterSchemaJson).RootElement;
    }

    public string Name { get; }
    public string Description { get; }
    public ToolCategory Category => ToolCategory.Mcp;
    public JsonElement ParameterSchema { get; }

    /// <summary>The MCP server that provides this tool.</summary>
    public string ServerName => _serverName;

    /// <summary>The original tool name on the MCP server (without namespace prefix).</summary>
    public string OriginalToolName => _originalToolName;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        try
        {
            var argsJson = arguments.ValueKind == JsonValueKind.Undefined
                ? "{}"
                : arguments.GetRawText();

            var result = await _serverManager.CallToolAsync(_serverName, _originalToolName, argsJson, ct);
            return ToolResult.Ok(result);
        }
        catch (McpException ex)
        {
            return ToolResult.Fail($"MCP tool error: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail($"MCP server not available: {ex.Message}");
        }
    }
}
