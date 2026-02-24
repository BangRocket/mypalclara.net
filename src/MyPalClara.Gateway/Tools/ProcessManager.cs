using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public class ProcessManagerService
{
    private readonly ConcurrentDictionary<string, TrackedProcess> _processes = new();
    private const int MaxOutputLines = 1000;

    public record TrackedProcess(
        string Pid, string Command, Process Process, DateTime StartedAt,
        ConcurrentQueue<string> OutputBuffer);

    public (string Pid, TrackedProcess Tracked) StartProcess(string command)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd" : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (isWindows) { psi.ArgumentList.Add("/c"); psi.ArgumentList.Add(command); }
        else { psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(command); }

        var process = new Process { StartInfo = psi };
        var buffer = new ConcurrentQueue<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                buffer.Enqueue(e.Data);
                while (buffer.Count > MaxOutputLines) buffer.TryDequeue(out string? _);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                buffer.Enqueue($"[stderr] {e.Data}");
                while (buffer.Count > MaxOutputLines) buffer.TryDequeue(out string? _);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var pid = process.Id.ToString();
        var tracked = new TrackedProcess(pid, command, process, DateTime.UtcNow, buffer);
        _processes[pid] = tracked;
        return (pid, tracked);
    }

    public TrackedProcess? GetProcess(string pid) =>
        _processes.TryGetValue(pid, out var p) ? p : null;

    public IReadOnlyList<TrackedProcess> ListAll() => _processes.Values.ToList();

    public bool StopProcess(string pid, bool force = false)
    {
        if (!_processes.TryRemove(pid, out var tracked))
            return false;

        try
        {
            if (!tracked.Process.HasExited)
                tracked.Process.Kill(entireProcessTree: true);
        }
        catch { }
        return true;
    }
}

public static class ProcessManagerTools
{
    public static void Register(IToolRegistry registry, ProcessManagerService pm)
    {
        registry.RegisterTool("process_start", new ToolSchema("process_start",
            "Start a background process. Args: command (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}""").RootElement),
            (args, ctx, ct) => StartAsync(args, ctx, pm, ct));

        registry.RegisterTool("process_status", new ToolSchema("process_status",
            "Get status of a tracked process. Args: pid (string).",
            JsonDocument.Parse("""{"type":"object","properties":{"pid":{"type":"string"}},"required":["pid"]}""").RootElement),
            (args, ctx, ct) => StatusAsync(args, ctx, pm, ct));

        registry.RegisterTool("process_output", new ToolSchema("process_output",
            "Get recent output from a tracked process. Args: pid (string), lines (int, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"pid":{"type":"string"},"lines":{"type":"integer"}},"required":["pid"]}""").RootElement),
            (args, ctx, ct) => OutputAsync(args, ctx, pm, ct));

        registry.RegisterTool("process_stop", new ToolSchema("process_stop",
            "Stop a tracked process. Args: pid (string), force (bool, optional).",
            JsonDocument.Parse("""{"type":"object","properties":{"pid":{"type":"string"},"force":{"type":"boolean"}},"required":["pid"]}""").RootElement),
            (args, ctx, ct) => StopAsync(args, ctx, pm, ct));

        registry.RegisterTool("process_list", new ToolSchema("process_list",
            "List all tracked processes.",
            JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement),
            (args, ctx, ct) => ListAsync(args, ctx, pm, ct));
    }

    public static Task<ToolResult> StartAsync(Dictionary<string, JsonElement> args, ToolCallContext ctx,
        ProcessManagerService pm, CancellationToken ct)
    {
        if (!args.TryGetValue("command", out var cmdElem))
            return Task.FromResult(new ToolResult(false, "", "Missing: command"));

        var (pid, _) = pm.StartProcess(cmdElem.GetString()!);
        return Task.FromResult(new ToolResult(true, JsonSerializer.Serialize(new { pid, status = "running" })));
    }

    public static Task<ToolResult> StatusAsync(Dictionary<string, JsonElement> args, ToolCallContext ctx,
        ProcessManagerService pm, CancellationToken ct)
    {
        if (!args.TryGetValue("pid", out var pidElem))
            return Task.FromResult(new ToolResult(false, "", "Missing: pid"));

        var tracked = pm.GetProcess(pidElem.GetString()!);
        if (tracked is null) return Task.FromResult(new ToolResult(false, "", "Process not found"));

        var running = !tracked.Process.HasExited;
        var uptime = DateTime.UtcNow - tracked.StartedAt;
        return Task.FromResult(new ToolResult(true,
            $"PID: {tracked.Pid}, Command: {tracked.Command}, Running: {running}, Uptime: {uptime:hh\\:mm\\:ss}"));
    }

    public static Task<ToolResult> OutputAsync(Dictionary<string, JsonElement> args, ToolCallContext ctx,
        ProcessManagerService pm, CancellationToken ct)
    {
        if (!args.TryGetValue("pid", out var pidElem))
            return Task.FromResult(new ToolResult(false, "", "Missing: pid"));

        var tracked = pm.GetProcess(pidElem.GetString()!);
        if (tracked is null) return Task.FromResult(new ToolResult(false, "", "Process not found"));

        var lines = args.TryGetValue("lines", out var lElem) ? lElem.GetInt32() : 50;
        var output = tracked.OutputBuffer.TakeLast(lines);
        return Task.FromResult(new ToolResult(true, string.Join("\n", output)));
    }

    public static Task<ToolResult> StopAsync(Dictionary<string, JsonElement> args, ToolCallContext ctx,
        ProcessManagerService pm, CancellationToken ct)
    {
        if (!args.TryGetValue("pid", out var pidElem))
            return Task.FromResult(new ToolResult(false, "", "Missing: pid"));

        var force = args.TryGetValue("force", out var fElem) && fElem.GetBoolean();
        var stopped = pm.StopProcess(pidElem.GetString()!, force);
        return Task.FromResult(stopped
            ? new ToolResult(true, "Process stopped")
            : new ToolResult(false, "", "Process not found"));
    }

    public static Task<ToolResult> ListAsync(Dictionary<string, JsonElement> args, ToolCallContext ctx,
        ProcessManagerService pm, CancellationToken ct)
    {
        var all = pm.ListAll();
        if (all.Count == 0)
            return Task.FromResult(new ToolResult(true, "No tracked processes."));

        var sb = new StringBuilder();
        foreach (var p in all)
        {
            var running = !p.Process.HasExited;
            sb.AppendLine($"PID={p.Pid} CMD={p.Command} RUNNING={running}");
        }
        return Task.FromResult(new ToolResult(true, sb.ToString().TrimEnd()));
    }
}
