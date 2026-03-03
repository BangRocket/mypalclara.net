namespace Clara.Core.Prompt;

/// <summary>
/// Reads tools.md from the workspace directory.
/// </summary>
public class ToolConventionsSection : IPromptSection
{
    public string Name => "tool-conventions";
    public int Priority => 100;

    public async Task<string?> GetContentAsync(PromptContext context, CancellationToken ct = default)
    {
        if (context.WorkspaceDir is null) return null;

        var path = Path.Combine(context.WorkspaceDir, "tools.md");
        if (!File.Exists(path)) return null;

        return await File.ReadAllTextAsync(path, ct);
    }
}
