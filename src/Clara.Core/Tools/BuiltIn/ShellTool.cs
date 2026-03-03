using System.Diagnostics;
using System.Text.Json;

namespace Clara.Core.Tools.BuiltIn;

public class ShellTool : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "command": { "type": "string", "description": "Shell command to execute" },
                "timeout_seconds": { "type": "integer", "description": "Timeout in seconds (default 30)" }
            },
            "required": ["command"]
        }
        """).RootElement;

    public string Name => "shell_execute";
    public string Description => "Execute a shell command and return combined stdout+stderr output";
    public ToolCategory Category => ToolCategory.Shell;
    public JsonElement ParameterSchema => Schema;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("command", out var commandEl))
            return ToolResult.Fail("Missing required parameter: command");

        var command = commandEl.GetString();
        if (string.IsNullOrWhiteSpace(command))
            return ToolResult.Fail("Command cannot be empty");

        var timeoutSeconds = 30;
        if (arguments.TryGetProperty("timeout_seconds", out var timeoutEl))
            timeoutSeconds = timeoutEl.GetInt32();

        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var psi = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd" : "/bin/sh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = context.WorkspaceDir ?? Environment.CurrentDirectory,
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
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return ToolResult.Fail($"Command timed out after {timeoutSeconds} seconds");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combined = string.IsNullOrEmpty(stderr)
                ? stdout
                : $"{stdout}\n--- stderr ---\n{stderr}";

            if (process.ExitCode != 0)
                return ToolResult.Fail($"Exit code {process.ExitCode}\n{combined}");

            return ToolResult.Ok(combined);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to execute command: {ex.Message}");
        }
    }
}
