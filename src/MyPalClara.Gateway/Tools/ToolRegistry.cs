using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, (ToolSchema Schema, Func<Dictionary<string, JsonElement>, ToolCallContext, CancellationToken, Task<ToolResult>> Handler)> _tools = new();
    private readonly List<IToolSource> _sources = [];
    private readonly object _sourceLock = new();
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = logger;
    }

    public void RegisterTool(string name, ToolSchema schema, Func<Dictionary<string, JsonElement>, ToolCallContext, CancellationToken, Task<ToolResult>> handler)
    {
        if (!_tools.TryAdd(name, (schema, handler)))
            throw new InvalidOperationException($"Tool '{name}' is already registered.");

        _logger.LogDebug("Registered tool: {Name}", name);
    }

    public void RegisterSource(IToolSource source)
    {
        lock (_sourceLock)
        {
            _sources.Add(source);
        }
        _logger.LogInformation("Registered tool source: {Name} ({Count} tools)",
            source.Name, source.GetTools().Count);
    }

    public void UnregisterTool(string name)
    {
        _tools.TryRemove(name, out _);
        _logger.LogDebug("Unregistered tool: {Name}", name);
    }

    public IReadOnlyList<ToolSchema> GetAllTools(ToolFilter? filter = null)
    {
        var result = new List<ToolSchema>();

        foreach (var (_, (schema, _)) in _tools)
            result.Add(schema);

        List<IToolSource> snapshot;
        lock (_sourceLock)
        {
            snapshot = [.. _sources];
        }

        foreach (var source in snapshot)
        {
            try
            {
                foreach (var tool in source.GetTools())
                {
                    if (!_tools.ContainsKey(tool.Name))
                        result.Add(tool);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get tools from source {Name}", source.Name);
            }
        }

        return result;
    }

    public async Task<ToolResult> ExecuteAsync(string name, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default)
    {
        if (_tools.TryGetValue(name, out var entry))
        {
            try
            {
                return await entry.Handler(args, context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool '{Name}' handler threw an exception", name);
                return new ToolResult(false, "", $"Tool '{name}' failed: {ex.Message}");
            }
        }

        List<IToolSource> snapshot;
        lock (_sourceLock)
        {
            snapshot = [.. _sources];
        }

        foreach (var source in snapshot)
        {
            if (source.CanHandle(name))
            {
                try
                {
                    return await source.ExecuteAsync(name, args, context, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Tool source '{Source}' failed executing '{Tool}'", source.Name, name);
                    return new ToolResult(false, "", $"Tool '{name}' failed: {ex.Message}");
                }
            }
        }

        return new ToolResult(false, "", $"Unknown tool: '{name}'. No handler or source registered for this tool.");
    }
}
