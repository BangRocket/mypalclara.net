using System.Text.Json;

namespace Clara.Core.Tools.BuiltIn;

public abstract class FileToolBase : ITool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public ToolCategory Category => ToolCategory.FileSystem;
    public abstract JsonElement ParameterSchema { get; }
    public abstract Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default);

    protected static string? ValidatePath(string path, ToolExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.WorkspaceDir))
            return null; // No workspace restriction

        var fullPath = Path.GetFullPath(path);
        var workspaceFull = Path.GetFullPath(context.WorkspaceDir);
        if (!fullPath.StartsWith(workspaceFull, StringComparison.OrdinalIgnoreCase))
            return $"Path '{path}' is outside workspace '{context.WorkspaceDir}'";

        return null;
    }
}

public class FileReadTool : FileToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "File path to read" }
            },
            "required": ["path"]
        }
        """).RootElement;

    public override string Name => "file_read";
    public override string Description => "Read the contents of a file";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("path", out var pathEl))
            return ToolResult.Fail("Missing required parameter: path");

        var path = pathEl.GetString()!;
        var violation = ValidatePath(path, context);
        if (violation is not null) return ToolResult.Fail(violation);

        if (!File.Exists(path))
            return ToolResult.Fail($"File not found: {path}");

        var content = await File.ReadAllTextAsync(path, ct);
        return ToolResult.Ok(content);
    }
}

public class FileWriteTool : FileToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "File path to write" },
                "content": { "type": "string", "description": "Content to write" }
            },
            "required": ["path", "content"]
        }
        """).RootElement;

    public override string Name => "file_write";
    public override string Description => "Write content to a file (creates or overwrites)";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("path", out var pathEl))
            return ToolResult.Fail("Missing required parameter: path");
        if (!arguments.TryGetProperty("content", out var contentEl))
            return ToolResult.Fail("Missing required parameter: content");

        var path = pathEl.GetString()!;
        var violation = ValidatePath(path, context);
        if (violation is not null) return ToolResult.Fail(violation);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(path, contentEl.GetString() ?? "", ct);
        return ToolResult.Ok($"Written to {path}");
    }
}

public class FileListTool : FileToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "Directory path to list" }
            },
            "required": ["path"]
        }
        """).RootElement;

    public override string Name => "file_list";
    public override string Description => "List files and directories in a path";
    public override JsonElement ParameterSchema => Schema;

    public override Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("path", out var pathEl))
            return Task.FromResult(ToolResult.Fail("Missing required parameter: path"));

        var path = pathEl.GetString()!;
        var violation = ValidatePath(path, context);
        if (violation is not null) return Task.FromResult(ToolResult.Fail(violation));

        if (!Directory.Exists(path))
            return Task.FromResult(ToolResult.Fail($"Directory not found: {path}"));

        var entries = Directory.GetFileSystemEntries(path)
            .Select(e =>
            {
                var name = Path.GetFileName(e);
                var isDir = Directory.Exists(e);
                return isDir ? $"[DIR]  {name}" : $"[FILE] {name}";
            });

        return Task.FromResult(ToolResult.Ok(string.Join('\n', entries)));
    }
}

public class FileDeleteTool : FileToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "File path to delete" }
            },
            "required": ["path"]
        }
        """).RootElement;

    public override string Name => "file_delete";
    public override string Description => "Delete a file";
    public override JsonElement ParameterSchema => Schema;

    public override Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("path", out var pathEl))
            return Task.FromResult(ToolResult.Fail("Missing required parameter: path"));

        var path = pathEl.GetString()!;
        var violation = ValidatePath(path, context);
        if (violation is not null) return Task.FromResult(ToolResult.Fail(violation));

        if (!File.Exists(path))
            return Task.FromResult(ToolResult.Fail($"File not found: {path}"));

        File.Delete(path);
        return Task.FromResult(ToolResult.Ok($"Deleted: {path}"));
    }
}
