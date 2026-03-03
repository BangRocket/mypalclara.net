namespace Clara.Core.Prompt;

/// <summary>
/// Reads per-user context from {WorkspaceDir}/users/{userId}/user.md.
/// </summary>
public class UserContextSection : IPromptSection
{
    public string Name => "user-context";
    public int Priority => 200;

    public async Task<string?> GetContentAsync(PromptContext context, CancellationToken ct = default)
    {
        if (context.WorkspaceDir is null) return null;

        var path = Path.Combine(context.WorkspaceDir, "users", context.UserId, "user.md");
        if (!File.Exists(path)) return null;

        return await File.ReadAllTextAsync(path, ct);
    }
}
