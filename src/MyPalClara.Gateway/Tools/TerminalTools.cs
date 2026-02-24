using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class TerminalTools
{
    private static readonly ConcurrentQueue<string> CommandHistory = new();
    private const int MaxHistory = 100;

    public static void Register(IToolRegistry registry)
    {
        var executeSchema = new ToolSchema("execute_command",
            "Execute a shell command and return stdout/stderr. Args: command (string), timeout_seconds (int, optional, default 30).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "command": { "type": "string", "description": "Shell command to execute" },
                    "timeout_seconds": { "type": "integer", "description": "Timeout in seconds (default 30)" }
                },
                "required": ["command"]
            }
            """).RootElement);

        registry.RegisterTool("execute_command", executeSchema, ExecuteCommandAsync);

        var historySchema = new ToolSchema("get_command_history",
            "Get recent command execution history. Args: limit (int, optional, default 20).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "limit": { "type": "integer", "description": "Number of recent commands to return (default 20)" }
                }
            }
            """).RootElement);

        registry.RegisterTool("get_command_history", historySchema, GetCommandHistoryAsync);
    }

    public static async Task<ToolResult> ExecuteCommandAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        if (!args.TryGetValue("command", out var cmdElem))
            return new ToolResult(false, "", "Missing required argument: command");

        var command = cmdElem.GetString() ?? "";
        var timeout = args.TryGetValue("timeout_seconds", out var tElem) ? tElem.GetInt32() : 30;

        // Record in history
        CommandHistory.Enqueue($"[{DateTime.UtcNow:HH:mm:ss}] {command}");
        while (CommandHistory.Count > MaxHistory) CommandHistory.TryDequeue(out string? _);

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd" : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (isWindows)
        {
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            process.WaitForExit(); // ensure async reads complete

            var output = stdout.ToString().TrimEnd();
            var error = stderr.ToString().TrimEnd();
            var combined = string.IsNullOrEmpty(error)
                ? output
                : $"{output}\n\nSTDERR:\n{error}";

            return new ToolResult(process.ExitCode == 0, combined,
                process.ExitCode != 0 ? $"Exit code: {process.ExitCode}" : null);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ToolResult(false, stdout.ToString().TrimEnd(), "Command timed out");
        }
    }

    public static Task<ToolResult> GetCommandHistoryAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        var limit = args.TryGetValue("limit", out var lElem) ? lElem.GetInt32() : 20;
        var items = CommandHistory.TakeLast(limit).ToList();

        if (items.Count == 0)
            return Task.FromResult(new ToolResult(true, "No command history."));

        return Task.FromResult(new ToolResult(true, string.Join("\n", items)));
    }
}
