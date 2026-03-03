using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Tools.Mcp;

/// <summary>
/// MCP client that communicates with an MCP server over stdio using JSON-RPC 2.0.
/// Spawns the server as a child process and communicates via stdin/stdout.
/// </summary>
public class McpStdioClient : IMcpClient
{
    private readonly string _command;
    private readonly string? _args;
    private readonly Dictionary<string, string>? _env;
    private readonly ILogger<McpStdioClient> _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Process? _process;
    private int _nextId;

    public McpStdioClient(
        string serverName,
        string command,
        string? args = null,
        Dictionary<string, string>? env = null,
        ILogger<McpStdioClient>? logger = null)
    {
        ServerName = serverName;
        _command = command;
        _args = args;
        _env = env;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<McpStdioClient>.Instance;
    }

    public string ServerName { get; }
    public bool IsConnected => _process is not null && !_process.HasExited;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected)
            throw new InvalidOperationException($"MCP server '{ServerName}' is already connected.");

        var psi = new ProcessStartInfo
        {
            FileName = _command,
            Arguments = _args ?? "",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (_env is not null)
        {
            foreach (var (key, value) in _env)
                psi.Environment[key] = value;
        }

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start MCP server process: {_command}");

        _logger.LogInformation("Started MCP server '{ServerName}' (PID {Pid})", ServerName, _process.Id);

        // Send initialize handshake
        var initResult = await SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "Clara",
                ["version"] = "1.0.0"
            }
        }, ct);

        _logger.LogDebug("MCP server '{ServerName}' initialized: {Result}", ServerName, initResult);

        // Send initialized notification (no id = notification)
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
        if (_process is null) return;

        try
        {
            // Send shutdown notification
            await SendNotificationAsync("notifications/cancelled", new JsonObject
            {
                ["reason"] = "client_shutdown"
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error sending shutdown notification to MCP server '{ServerName}'", ServerName);
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error killing MCP server process '{ServerName}'", ServerName);
        }

        _process.Dispose();
        _process = null;
        _logger.LogInformation("Disconnected MCP server '{ServerName}'", ServerName);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<JsonNode?> SendRequestAsync(string method, JsonObject? parameters, CancellationToken ct)
    {
        EnsureConnected();
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
        _logger.LogTrace("MCP [{ServerName}] -> {Request}", ServerName, requestJson);

        string? responseLine;
        await _sendLock.WaitAsync(ct);
        try
        {
            await _process!.StandardInput.WriteLineAsync(requestJson.AsMemory(), ct);
            await _process.StandardInput.FlushAsync();

            // Read response line — MCP stdio protocol is newline-delimited JSON
            responseLine = await _process.StandardOutput.ReadLineAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }

        if (responseLine is null)
            throw new InvalidOperationException($"MCP server '{ServerName}' closed stdout unexpectedly.");

        _logger.LogTrace("MCP [{ServerName}] <- {Response}", ServerName, responseLine);

        var response = JsonNode.Parse(responseLine);
        if (response is not JsonObject responseObj)
            throw new InvalidOperationException($"MCP server '{ServerName}' returned invalid JSON-RPC response.");

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
        EnsureConnected();

        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };
        if (parameters is not null)
            notification["params"] = parameters;

        var json = notification.ToJsonString();
        _logger.LogTrace("MCP [{ServerName}] -> (notification) {Request}", ServerName, json);

        await _sendLock.WaitAsync(ct);
        try
        {
            await _process!.StandardInput.WriteLineAsync(json.AsMemory(), ct);
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException($"MCP server '{ServerName}' is not connected.");
    }
}

public class McpException : Exception
{
    public string ServerName { get; }
    public string Method { get; }
    public int ErrorCode { get; }

    public McpException(string serverName, string method, int errorCode, string message)
        : base($"MCP server '{serverName}' returned error for '{method}': [{errorCode}] {message}")
    {
        ServerName = serverName;
        Method = method;
        ErrorCode = errorCode;
    }
}
