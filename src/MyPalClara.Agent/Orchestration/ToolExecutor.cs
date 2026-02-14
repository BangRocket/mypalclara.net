using System.Diagnostics;
using System.Text.Json;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Data;
using MyPalClara.Core.Data.Models;
using MyPalClara.Core.Llm;
using MyPalClara.Agent.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Agent.Orchestration;

/// <summary>Routes tool calls from the LLM to the appropriate MCP server.</summary>
public sealed class ToolExecutor
{
    private readonly McpServerManager _mcpManager;
    private readonly ToolPolicyEvaluator _policy;
    private readonly IDbContextFactory<ClaraDbContext>? _dbFactory;
    private readonly ClaraConfig _config;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(
        McpServerManager mcpManager,
        ToolPolicyEvaluator policy,
        ClaraConfig config,
        ILogger<ToolExecutor> logger,
        IDbContextFactory<ClaraDbContext>? dbFactory = null)
    {
        _mcpManager = mcpManager;
        _policy = policy;
        _config = config;
        _logger = logger;
        _dbFactory = dbFactory;
    }

    /// <summary>Execute a tool call and return the result text.</summary>
    public async Task<string> ExecuteAsync(ToolCall toolCall, CancellationToken ct = default)
    {
        var decision = _policy.Evaluate(toolCall.Name);
        _logger.LogDebug("Tool {Tool} policy decision: {Decision}", toolCall.Name, decision);

        if (decision == ToolDecision.Blocked)
        {
            LogToolCall(toolCall, "blocked", null, false, "Tool blocked by policy");
            return $"Error: Tool '{toolCall.Name}' is blocked by security policy.";
        }

        if (decision == ToolDecision.RequiresApproval)
        {
            LogToolCall(toolCall, "requires_approval", null, false, "Tool requires approval");
            return $"[TOOL_BLOCKED: Tool '{toolCall.Name}' requires approval before execution.]";
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_config.ToolSecurity.MaxExecutionSeconds));

            var result = await _mcpManager.CallToolAsync(toolCall.Name, toolCall.Arguments, timeoutCts.Token);
            sw.Stop();

            _logger.LogDebug("Tool {Tool} returned {Len} chars in {Ms}ms", toolCall.Name, result.Length, sw.ElapsedMilliseconds);
            LogToolCall(toolCall, "allowed", result, true, latencyMs: (int)sw.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            var msg = $"Tool '{toolCall.Name}' timed out after {_config.ToolSecurity.MaxExecutionSeconds}s";
            _logger.LogWarning(msg);
            LogToolCall(toolCall, "allowed", null, false, msg, (int)sw.ElapsedMilliseconds);
            return $"Error: {msg}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Tool execution failed: {Tool}", toolCall.Name);
            LogToolCall(toolCall, "allowed", null, false, ex.Message, (int)sw.ElapsedMilliseconds);
            return $"Error: Tool '{toolCall.Name}' failed: {ex.Message}";
        }
    }

    private void LogToolCall(ToolCall toolCall, string decision, string? result, bool success, string? errorMessage = null, int? latencyMs = null)
    {
        if (!_config.ToolSecurity.LogAllCalls || _dbFactory is null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                db.ToolCalls.Add(new ToolCallEntity
                {
                    ToolName = toolCall.Name,
                    Arguments = JsonSerializer.Serialize(toolCall.Arguments),
                    Result = result?[..Math.Min(result.Length, 10_000)],
                    Decision = decision,
                    Success = success,
                    ErrorMessage = errorMessage,
                    LatencyMs = latencyMs,
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to log tool call for {Tool}", toolCall.Name);
            }
        });
    }
}
