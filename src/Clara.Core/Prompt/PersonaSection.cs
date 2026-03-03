namespace Clara.Core.Prompt;

/// <summary>
/// Reads persona.md from the workspace directory.
/// </summary>
public class PersonaSection : IPromptSection
{
    public string Name => "persona";
    public int Priority => 0;

    public async Task<string?> GetContentAsync(PromptContext context, CancellationToken ct = default)
    {
        if (context.WorkspaceDir is null) return null;

        var path = Path.Combine(context.WorkspaceDir, "persona.md");
        if (!File.Exists(path)) return null;

        return await File.ReadAllTextAsync(path, ct);
    }
}
