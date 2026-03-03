using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Tools.Mcp;

/// <summary>
/// MCP client that communicates with an MCP server over HTTP using JSON-RPC 2.0.
/// Each request is a POST to the server's endpoint; optionally uses SSE for events.
/// </summary>
public class McpHttpClient : IMcpClient
{
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly ILogger<McpHttpClient> _logger;
    private int _nextId;
    private bool _connected;

    public McpHttpClient(
        string serverName,
        string baseUrl,
        HttpClient? httpClient = null,
        ILogger<McpHttpClient>? logger = null)
    {
        ServerName = serverName;
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<McpHttpClient>.Instance;
    }

    public string ServerName { get; }
    public bool IsConnected => _connected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connected)
            throw new InvalidOperationException($"MCP server '{ServerName}' is already connected.");

        var result = await SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "Clara",
                ["version"] = "1.0.0"
            }
        }, ct);

        _connected = true;
        _logger.LogInformation("Connected to MCP HTTP server '{ServerName}' at {BaseUrl}", ServerName, _baseUrl);

        // Send initialized notification
        await SendNotificationAsync("notifications/initialized", new JsonObject(), ct);
    }

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        var result = await SendRequestAsync("tools/list", new JsonObject(), ct);

        var tools = new List<McpToolInfo>();
        if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("tools", out var toolsNode) && toolsNode is JsonArray toolsArray)
        {
            foreach (var toolNode in toolsArray)
            {
                if (toolNode is not JsonObject toolObj) continue;

                var name = toolObj["name"]?.GetValue<string>() ?? "";
                var description = toolObj["description"]?.GetValue<string>() ?? "";
                var inputSchema = toolObj["inputSchema"]?.ToJsonString() ?? "{}";

                tools.Add(new McpToolInfo(name, description, inputSchema));
            }
        }

        return tools;
    }

    public async Task<string> CallToolAsync(string toolName, string argumentsJson, CancellationToken ct = default)
    {
        EnsureConnected();

        var argsNode = string.IsNullOrWhiteSpace(argumentsJson)
            ? new JsonObject()
            : JsonNode.Parse(argumentsJson) as JsonObject ?? new JsonObject();

        var result = await SendRequestAsync("tools/call", new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = argsNode
        }, ct);

        // MCP tool results have a "content" array
        if (result is JsonObject resultObj && resultObj.TryGetPropertyValue("content", out var contentNode) && contentNode is JsonArray contentArray)
        {
            var sb = new StringBuilder();
            foreach (var item in contentArray)
            {
                if (item is JsonObject contentObj)
                {
                    var type = contentObj["type"]?.GetValue<string>();
                    if (type == "text")
                    {
                        var text = contentObj["text"]?.GetValue<string>();
                        if (text is not null) sb.Append(text);
                    }
                }
            }
            return sb.ToString();
        }

        return result?.ToJsonString() ?? "";
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (!_connected) return;

        try
        {
            await SendNotificationAsync("notifications/cancelled", new JsonObject
            {
                ["reason"] = "client_shutdown"
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error sending shutdown notification to MCP HTTP server '{ServerName}'", ServerName);
        }

        _connected = false;
        _logger.LogInformation("Disconnected from MCP HTTP server '{ServerName}'", ServerName);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }

    private async Task<JsonNode?> SendRequestAsync(string method, JsonObject? parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method
        };
        if (parameters is not null)
            request["params"] = parameters;

        var requestJson = request.ToJsonString();
        _logger.LogTrace("MCP HTTP [{ServerName}] -> {Request}", ServerName, requestJson);

        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        var httpResponse = await _httpClient.PostAsync($"{_baseUrl}/jsonrpc", content, ct);
        httpResponse.EnsureSuccessStatusCode();

        var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);
        _logger.LogTrace("MCP HTTP [{ServerName}] <- {Response}", ServerName, responseBody);

        var response = JsonNode.Parse(responseBody);
        if (response is not JsonObject responseObj)
            throw new InvalidOperationException($"MCP HTTP server '{ServerName}' returned invalid JSON-RPC response.");

        // Check for error
        if (responseObj.TryGetPropertyValue("error", out var errorNode) && errorNode is JsonObject errorObj)
        {
            var code = errorObj["code"]?.GetValue<int>() ?? -1;
            var message = errorObj["message"]?.GetValue<string>() ?? "Unknown error";
            throw new McpException(ServerName, method, code, message);
        }

        return responseObj["result"];
    }

    private async Task SendNotificationAsync(string method, JsonObject? parameters, CancellationToken ct)
    {
        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };
        if (parameters is not null)
            notification["params"] = parameters;

        var json = notification.ToJsonString();
        _logger.LogTrace("MCP HTTP [{ServerName}] -> (notification) {Request}", ServerName, json);

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            await _httpClient.PostAsync($"{_baseUrl}/jsonrpc", content, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send notification '{Method}' to MCP HTTP server '{ServerName}'", method, ServerName);
        }
    }

    private void EnsureConnected()
    {
        if (!_connected)
            throw new InvalidOperationException($"MCP HTTP server '{ServerName}' is not connected.");
    }
}
