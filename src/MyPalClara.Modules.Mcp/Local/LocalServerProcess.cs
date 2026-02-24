using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Mcp.Models;

namespace MyPalClara.Modules.Mcp.Local;

/// <summary>
/// Manages a single MCP server subprocess communicating via JSON-RPC over stdin/stdout.
/// </summary>
public class LocalServerProcess : IAsyncDisposable
{
    private Process? _process;
    private readonly McpServerConfig _config;
    private readonly ILogger _logger;
    private readonly List<McpTool> _tools = [];
    private int _nextId = 1;

    public string Name => _config.Name;
    public bool IsRunning => _process is { HasExited: false };
    public IReadOnlyList<McpTool> Tools => _tools;

    public LocalServerProcess(McpServerConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_config.Command is null) throw new InvalidOperationException("No command configured");

        var psi = new ProcessStartInfo
        {
            FileName = _config.Command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (_config.Args is not null)
            foreach (var arg in _config.Args)
                psi.ArgumentList.Add(arg);

        if (_config.Env is not null)
            foreach (var (k, v) in _config.Env)
                psi.Environment[k] = v;

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start MCP server: {_config.Name}");

        // Initialize
        await SendRpcAsync("initialize", new { protocolVersion = "2024-11-05",
            capabilities = new { }, clientInfo = new { name = "clara", version = "1.0" } }, ct);

        // Discover tools
        var toolsResult = await SendRpcAsync("tools/list", new { }, ct);
        if (toolsResult.TryGetProperty("tools", out var toolsArray))
        {
            foreach (var t in toolsArray.EnumerateArray())
            {
                var name = t.GetProperty("name").GetString()!;
                var desc = t.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var schema = t.TryGetProperty("inputSchema", out var s) ? s : JsonDocument.Parse("{}").RootElement;
                _tools.Add(new McpTool(name, desc, schema));
            }
        }

        _logger.LogInformation("MCP server {Name} started with {Count} tools", Name, _tools.Count);
    }

    public async Task<JsonElement> CallToolAsync(string toolName, Dictionary<string, JsonElement> args,
        CancellationToken ct = default)
    {
        var argsObj = new JsonObject();
        foreach (var (k, v) in args)
            argsObj[k] = JsonNode.Parse(v.GetRawText());

        return await SendRpcAsync("tools/call", new { name = toolName, arguments = argsObj }, ct);
    }

    private async Task<JsonElement> SendRpcAsync(string method, object @params, CancellationToken ct)
    {
        if (_process?.StandardInput is null)
            throw new InvalidOperationException("Process not started");

        var id = Interlocked.Increment(ref _nextId);
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params });

        await _process.StandardInput.WriteLineAsync(request.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);

        var responseLine = await _process.StandardOutput.ReadLineAsync(ct)
            ?? throw new InvalidOperationException("No response from MCP server");

        using var doc = JsonDocument.Parse(responseLine);
        if (doc.RootElement.TryGetProperty("result", out var result))
            return result.Clone();
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"MCP RPC error: {error.GetRawText()}");

        return JsonDocument.Parse("{}").RootElement;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
        }
        _process?.Dispose();
    }
}
