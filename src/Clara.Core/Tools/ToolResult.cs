namespace Clara.Core.Tools;

public record ToolResult(bool Success, string Content, string? Error = null)
{
    public static ToolResult Ok(string content) => new(true, content);
    public static ToolResult Fail(string error) => new(false, "", error);
}
