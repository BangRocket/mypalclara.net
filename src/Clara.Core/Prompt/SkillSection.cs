namespace Clara.Core.Prompt;

/// <summary>
/// Placeholder for runtime skill injection into the prompt.
/// </summary>
public class SkillSection : IPromptSection
{
    public string Name => "skills";
    public int Priority => 400;

    public Task<string?> GetContentAsync(PromptContext context, CancellationToken ct = default)
    {
        // Placeholder — skills will inject their context at runtime
        return Task.FromResult<string?>(null);
    }
}
