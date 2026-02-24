using System.Diagnostics;
using System.Text;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Hooks;

public static class ShellExecutor
{
    public static async Task<(bool Success, string Output, string Error)> ExecuteAsync(
        string command, GatewayEvent evt, string? workingDir = null,
        double timeoutSeconds = 30.0, CancellationToken ct = default)
    {
        var env = BuildEnvironment(evt);
        var expandedCommand = ExpandVariables(command, env);

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd" : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory()
        };

        // Use ArgumentList to avoid shell argument splitting issues.
        // Each element is passed as a distinct argv entry.
        if (isWindows)
        {
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(expandedCommand);
        }
        else
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(expandedCommand);
        }

        foreach (var (key, value) in env)
            psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            // Synchronous WaitForExit() ensures all async output/error reads have completed.
            // Without this, OutputDataReceived/ErrorDataReceived events may not have fired yet.
            process.WaitForExit();
            return (process.ExitCode == 0, stdout.ToString().TrimEnd(), stderr.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (false, stdout.ToString().TrimEnd(), "Timed out");
        }
    }

    public static Dictionary<string, string> BuildEnvironment(GatewayEvent evt)
    {
        var env = new Dictionary<string, string>
        {
            ["CLARA_EVENT_TYPE"] = evt.Type,
            ["CLARA_TIMESTAMP"] = evt.Timestamp.ToString("O")
        };

        if (evt.NodeId != null) env["CLARA_NODE_ID"] = evt.NodeId;
        if (evt.Platform != null) env["CLARA_PLATFORM"] = evt.Platform;
        if (evt.UserId != null) env["CLARA_USER_ID"] = evt.UserId;
        if (evt.ChannelId != null) env["CLARA_CHANNEL_ID"] = evt.ChannelId;
        if (evt.RequestId != null) env["CLARA_REQUEST_ID"] = evt.RequestId;

        if (evt.Data != null)
        {
            env["CLARA_EVENT_DATA"] = System.Text.Json.JsonSerializer.Serialize(evt.Data);
            foreach (var (key, value) in evt.Data)
            {
                if (value is string or int or long or double or bool)
                    env[$"CLARA_{key.ToUpperInvariant()}"] = value.ToString()!;
            }
        }

        return env;
    }

    public static string ExpandVariables(string command, Dictionary<string, string> env)
    {
        foreach (var (key, value) in env)
            command = command.Replace($"${{{key}}}", value);
        return command;
    }
}
