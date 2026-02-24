using System.Text.Json;

namespace MyPalClara.Modules.Mcp.Models;

public record McpTool(string Name, string Description, JsonElement InputSchema);
