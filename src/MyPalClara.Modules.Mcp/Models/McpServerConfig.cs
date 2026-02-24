namespace MyPalClara.Modules.Mcp.Models;

public record McpServerConfig(
    string Name, string ServerType, string? Command, string[]? Args,
    Dictionary<string, string>? Env, string? Endpoint, bool Enabled, string? ConfigPath);
