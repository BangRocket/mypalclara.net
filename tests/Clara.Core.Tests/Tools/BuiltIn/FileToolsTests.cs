using System.Text.Json;
using Clara.Core.Tools;
using Clara.Core.Tools.BuiltIn;

namespace Clara.Core.Tests.Tools.BuiltIn;

public class FileToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ToolExecutionContext _context;

    public FileToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"clara_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _context = new ToolExecutionContext("user1", "session1", "test", false, _tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // --- file_write ---

    [Fact]
    public async Task Write_creates_file()
    {
        var tool = new FileWriteTool();
        var filePath = Path.Combine(_tempDir, "test.txt");
        var args = JsonDocument.Parse($$"""{"path":"{{filePath}}","content":"hello world"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.True(result.Success);
        Assert.True(File.Exists(filePath));
        Assert.Equal("hello world", File.ReadAllText(filePath));
    }

    [Fact]
    public async Task Write_creates_subdirectories()
    {
        var tool = new FileWriteTool();
        var filePath = Path.Combine(_tempDir, "sub", "dir", "test.txt");
        var args = JsonDocument.Parse($$"""{"path":"{{filePath}}","content":"nested"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.True(result.Success);
        Assert.Equal("nested", File.ReadAllText(filePath));
    }

    // --- file_read ---

    [Fact]
    public async Task Read_returns_file_content()
    {
        var tool = new FileReadTool();
        var filePath = Path.Combine(_tempDir, "read_test.txt");
        File.WriteAllText(filePath, "test content");
        var args = JsonDocument.Parse($$"""{"path":"{{filePath}}"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.True(result.Success);
        Assert.Equal("test content", result.Content);
    }

    [Fact]
    public async Task Read_nonexistent_file_returns_failure()
    {
        var tool = new FileReadTool();
        var filePath = Path.Combine(_tempDir, "nonexistent.txt");
        var args = JsonDocument.Parse($$"""{"path":"{{filePath}}"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // --- file_list ---

    [Fact]
    public async Task List_shows_files_and_dirs()
    {
        var tool = new FileListTool();
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "");
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));
        var args = JsonDocument.Parse($$"""{"path":"{{_tempDir}}"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.True(result.Success);
        Assert.Contains("[FILE] a.txt", result.Content);
        Assert.Contains("[DIR]  subdir", result.Content);
    }

    [Fact]
    public async Task List_nonexistent_dir_returns_failure()
    {
        var tool = new FileListTool();
        var dirPath = Path.Combine(_tempDir, "nonexistent_dir");
        var args = JsonDocument.Parse($$"""{"path":"{{dirPath}}"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.False(result.Success);
    }

    // --- file_delete ---

    [Fact]
    public async Task Delete_removes_file()
    {
        var tool = new FileDeleteTool();
        var filePath = Path.Combine(_tempDir, "to_delete.txt");
        File.WriteAllText(filePath, "delete me");
        var args = JsonDocument.Parse($$"""{"path":"{{filePath}}"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.True(result.Success);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task Delete_nonexistent_file_returns_failure()
    {
        var tool = new FileDeleteTool();
        var filePath = Path.Combine(_tempDir, "nonexistent.txt");
        var args = JsonDocument.Parse($$"""{"path":"{{filePath}}"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.False(result.Success);
    }

    // --- Path validation ---

    [Fact]
    public async Task Write_outside_workspace_is_denied()
    {
        var tool = new FileWriteTool();
        var args = JsonDocument.Parse("""{"path":"/tmp/outside.txt","content":"bad"}""").RootElement;

        var result = await tool.ExecuteAsync(args, _context);

        Assert.False(result.Success);
        Assert.Contains("outside workspace", result.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
