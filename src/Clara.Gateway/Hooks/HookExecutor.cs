using System.Diagnostics;
using System.Text;
using Clara.Core.Events;
using Microsoft.Extensions.Logging;

namespace Clara.Gateway.Hooks;

public class HookExecutor
{
    private readonly ILogger<HookExecutor> _logger;

    public HookExecutor(ILogger<HookExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> ExecuteAsync(HookDefinition hook, ClaraEvent? evt = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Executing hook {HookName} ({Command})", hook.Name, hook.Command);

        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            ArgumentList = { OperatingSystem.IsWindows() ? "/c" : "-c", hook.Command },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (hook.WorkingDir is not null)
            psi.WorkingDirectory = hook.WorkingDir;

        // Set environment variables from event
        if (evt is not null)
        {
            psi.Environment["CLARA_EVENT_TYPE"] = evt.Type;
            psi.Environment["CLARA_TIMESTAMP"] = evt.Timestamp.ToString("O");

            if (evt.UserId is not null)
                psi.Environment["CLARA_USER_ID"] = evt.UserId;
            if (evt.SessionKey is not null)
                psi.Environment["CLARA_SESSION_KEY"] = evt.SessionKey;
            if (evt.Platform is not null)
                psi.Environment["CLARA_PLATFORM"] = evt.Platform;
            if (evt.Data is not null)
                psi.Environment["CLARA_EVENT_DATA"] = System.Text.Json.JsonSerializer.Serialize(evt.Data);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return new HookResult(false, "", "Failed to start process");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(hook.TimeoutSeconds));

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            var stdoutTask = ReadStreamAsync(process.StandardOutput, stdout, cts.Token);
            var stderrTask = ReadStreamAsync(process.StandardError, stderr, cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
                await Task.WhenAll(stdoutTask, stderrTask);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                _logger.LogWarning("Hook {HookName} timed out after {Timeout}s", hook.Name, hook.TimeoutSeconds);
                return new HookResult(false, stdout.ToString(), $"Timed out after {hook.TimeoutSeconds}s");
            }

            var success = process.ExitCode == 0;
            if (!success)
                _logger.LogWarning("Hook {HookName} failed with exit code {ExitCode}: {Stderr}",
                    hook.Name, process.ExitCode, stderr.ToString().Trim());

            return new HookResult(success, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing hook {HookName}", hook.Name);
            return new HookResult(false, "", ex.Message);
        }
    }

    private static async Task ReadStreamAsync(StreamReader reader, StringBuilder sb, CancellationToken ct)
    {
        var buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer, ct)) > 0)
            sb.Append(buffer, 0, read);
    }
}

public record HookResult(bool Success, string Stdout, string Stderr);
