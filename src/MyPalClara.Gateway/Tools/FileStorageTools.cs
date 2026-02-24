using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class FileStorageTools
{
    public static void Register(IToolRegistry registry)
    {
        registry.RegisterTool("save_to_local", new ToolSchema("save_to_local",
            "Save content to a file in local storage. Args: filename (string), content (string).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "filename": { "type": "string" },
                    "content": { "type": "string" }
                },
                "required": ["filename", "content"]
            }
            """).RootElement), SaveToLocalAsync);

        registry.RegisterTool("list_local_files", new ToolSchema("list_local_files",
            "List files in local storage. Args: path (string, optional).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "path": { "type": "string" }
                }
            }
            """).RootElement), ListLocalFilesAsync);

        registry.RegisterTool("read_local_file", new ToolSchema("read_local_file",
            "Read a file from local storage. Args: filename (string).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "filename": { "type": "string" }
                },
                "required": ["filename"]
            }
            """).RootElement), ReadLocalFileAsync);

        registry.RegisterTool("delete_local_file", new ToolSchema("delete_local_file",
            "Delete a file from local storage. Args: filename (string).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "filename": { "type": "string" }
                },
                "required": ["filename"]
            }
            """).RootElement), DeleteLocalFileAsync);
    }

    public static async Task<ToolResult> SaveToLocalAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        var filesDir = Environment.GetEnvironmentVariable("CLARA_FILES_DIR") ?? "./clara_files";
        var userDir = Path.Combine(filesDir, ctx.UserId);
        Directory.CreateDirectory(userDir);

        if (!args.TryGetValue("filename", out var fnElem))
            return new ToolResult(false, "", "Missing required argument: filename");
        if (!args.TryGetValue("content", out var contentElem))
            return new ToolResult(false, "", "Missing required argument: content");

        var filename = Path.GetFileName(fnElem.GetString() ?? "file.txt");
        var content = contentElem.GetString() ?? "";
        var path = Path.Combine(userDir, filename);

        await File.WriteAllTextAsync(path, content, ct);
        return new ToolResult(true, $"Saved to {filename} ({content.Length} bytes)");
    }

    public static Task<ToolResult> ListLocalFilesAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        var filesDir = Environment.GetEnvironmentVariable("CLARA_FILES_DIR") ?? "./clara_files";
        var userDir = Path.Combine(filesDir, ctx.UserId);
        if (args.TryGetValue("path", out var pathElem))
            userDir = Path.Combine(userDir, pathElem.GetString() ?? "");

        if (!Directory.Exists(userDir))
            return Task.FromResult(new ToolResult(true, "No files found."));

        var files = Directory.GetFiles(userDir).Select(Path.GetFileName);
        return Task.FromResult(new ToolResult(true, string.Join("\n", files)));
    }

    public static async Task<ToolResult> ReadLocalFileAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        var filesDir = Environment.GetEnvironmentVariable("CLARA_FILES_DIR") ?? "./clara_files";
        if (!args.TryGetValue("filename", out var fnElem))
            return new ToolResult(false, "", "Missing required argument: filename");

        var filename = Path.GetFileName(fnElem.GetString() ?? "");
        var path = Path.Combine(filesDir, ctx.UserId, filename);

        if (!File.Exists(path))
            return new ToolResult(false, "", $"File not found: {filename}");

        var content = await File.ReadAllTextAsync(path, ct);
        return new ToolResult(true, content);
    }

    public static Task<ToolResult> DeleteLocalFileAsync(
        Dictionary<string, JsonElement> args, ToolCallContext ctx, CancellationToken ct)
    {
        var filesDir = Environment.GetEnvironmentVariable("CLARA_FILES_DIR") ?? "./clara_files";
        if (!args.TryGetValue("filename", out var fnElem))
            return Task.FromResult(new ToolResult(false, "", "Missing required argument: filename"));

        var filename = Path.GetFileName(fnElem.GetString() ?? "");
        var path = Path.Combine(filesDir, ctx.UserId, filename);

        if (!File.Exists(path))
            return Task.FromResult(new ToolResult(false, "", $"File not found: {filename}"));

        File.Delete(path);
        return Task.FromResult(new ToolResult(true, $"Deleted {filename}"));
    }
}
