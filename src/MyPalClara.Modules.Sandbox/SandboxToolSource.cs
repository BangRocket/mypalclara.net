using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Sandbox;

public class SandboxToolSource : IToolSource
{
    private readonly ISandboxManager _manager;

    private static readonly HashSet<string> ToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "execute_python", "run_shell", "install_package", "read_file",
        "write_file", "list_files", "unzip_file", "web_search"
    };

    public SandboxToolSource(ISandboxManager manager) => _manager = manager;

    public string Name => "sandbox";

    public IReadOnlyList<ToolSchema> GetTools() =>
    [
        new("execute_python", "Execute Python code in sandbox. Args: code (string), timeout (int, optional).", JsonDocument.Parse("""{"type":"object","properties":{"code":{"type":"string"},"timeout":{"type":"integer"}},"required":["code"]}""").RootElement),
        new("run_shell", "Run a shell command in sandbox. Args: command (string), timeout (int, optional).", JsonDocument.Parse("""{"type":"object","properties":{"command":{"type":"string"},"timeout":{"type":"integer"}},"required":["command"]}""").RootElement),
        new("install_package", "Install a Python package in sandbox. Args: package (string).", JsonDocument.Parse("""{"type":"object","properties":{"package":{"type":"string"}},"required":["package"]}""").RootElement),
        new("read_file", "Read a file from sandbox. Args: path (string).", JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""").RootElement),
        new("write_file", "Write a file in sandbox. Args: path (string), content (string).", JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}""").RootElement),
        new("list_files", "List files in sandbox. Args: path (string, optional).", JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""").RootElement),
        new("unzip_file", "Unzip a file in sandbox. Args: path (string), dest (string, optional).", JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"},"dest":{"type":"string"}},"required":["path"]}""").RootElement),
        new("web_search", "Search the web (Tavily). Args: query (string), max_results (int, optional).", JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"},"max_results":{"type":"integer"}},"required":["query"]}""").RootElement)
    ];

    public bool CanHandle(string toolName) => ToolNames.Contains(toolName);

    public async Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default)
    {
        switch (toolName)
        {
            case "execute_python":
                var code = args.TryGetValue("code", out var ce) ? ce.GetString()! : "";
                var timeout = args.TryGetValue("timeout", out var te) ? te.GetInt32() : (int?)null;
                var pyResult = await _manager.ExecutePythonAsync(context.UserId, code, timeout, ct);
                return new ToolResult(pyResult.Success, pyResult.Stdout,
                    pyResult.Success ? null : pyResult.Stderr);

            case "run_shell":
                var cmd = args.TryGetValue("command", out var cme) ? cme.GetString()! : "";
                var cmdTimeout = args.TryGetValue("timeout", out var cte) ? cte.GetInt32() : (int?)null;
                var shellResult = await _manager.ExecuteAsync(context.UserId, cmd, timeoutSeconds: cmdTimeout, ct: ct);
                return new ToolResult(shellResult.Success, shellResult.Stdout,
                    shellResult.Success ? null : shellResult.Stderr);

            case "install_package":
                var pkg = args.TryGetValue("package", out var pe) ? pe.GetString()! : "";
                var installResult = await _manager.ExecuteAsync(context.UserId, $"pip install {pkg}", ct: ct);
                return new ToolResult(installResult.Success, installResult.Stdout, installResult.Stderr);

            case "read_file":
                var readPath = args.TryGetValue("path", out var rpe) ? rpe.GetString()! : "";
                var content = await _manager.ReadFileAsync(context.UserId, readPath, ct);
                return new ToolResult(true, content);

            case "write_file":
                var writePath = args.TryGetValue("path", out var wpe) ? wpe.GetString()! : "";
                var writeContent = args.TryGetValue("content", out var wce) ? wce.GetString()! : "";
                await _manager.WriteFileAsync(context.UserId, writePath, writeContent, ct);
                return new ToolResult(true, $"Written to {writePath}");

            case "list_files":
                var listPath = args.TryGetValue("path", out var lpe) ? lpe.GetString() ?? "." : ".";
                var files = await _manager.ListFilesAsync(context.UserId, listPath, ct);
                return new ToolResult(true, string.Join("\n", files));

            case "unzip_file":
                var zipPath = args.TryGetValue("path", out var zpe) ? zpe.GetString()! : "";
                var dest = args.TryGetValue("dest", out var de) ? de.GetString() ?? "." : ".";
                var unzipResult = await _manager.ExecuteAsync(context.UserId, $"unzip -o {zipPath} -d {dest}", ct: ct);
                return new ToolResult(unzipResult.Success, unzipResult.Stdout, unzipResult.Stderr);

            case "web_search":
                var query = args.TryGetValue("query", out var qe) ? qe.GetString()! : "";
                // Tavily web search via API
                return new ToolResult(true, $"Web search results for: {query} (Tavily API not configured)");

            default:
                return new ToolResult(false, "", $"Unknown sandbox tool: {toolName}");
        }
    }
}
