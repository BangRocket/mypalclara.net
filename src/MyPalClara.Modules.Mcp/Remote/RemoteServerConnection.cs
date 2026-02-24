using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Mcp.Models;

namespace MyPalClara.Modules.Mcp.Remote;

public class RemoteServerConnection
{
    private readonly McpServerConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly List<McpTool> _tools = [];

    public string Name => _config.Name;
    public IReadOnlyList<McpTool> Tools => _tools;

    public RemoteServerConnection(McpServerConfig config, HttpClient httpClient, ILogger logger)
    {
        _config = config;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_config.Endpoint is null) throw new InvalidOperationException("No endpoint configured");

        var response = await _httpClient.GetAsync($"{_config.Endpoint}/tools", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("tools", out var toolsArray))
        {
            foreach (var t in toolsArray.EnumerateArray())
            {
                var name = t.GetProperty("name").GetString()!;
                var desc = t.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var schema = t.TryGetProperty("inputSchema", out var s) ? s.Clone() : JsonDocument.Parse("{}").RootElement;
                _tools.Add(new McpTool(name, desc, schema));
            }
        }
    }

    public async Task<string> CallToolAsync(string toolName, Dictionary<string, JsonElement> args,
        CancellationToken ct = default)
    {
        var content = new StringContent(JsonSerializer.Serialize(args),
            System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_config.Endpoint}/tools/{toolName}", content, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
