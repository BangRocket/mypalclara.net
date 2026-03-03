namespace Clara.Core.Prompt;

/// <summary>
/// Reads heartbeat.md from the workspace directory if applicable.
/// </summary>
public class HeartbeatSection : IPromptSection
{
    public string Name => "heartbeat";
    public int Priority => 500;

    public async Task<string?> GetContentAsync(PromptContext context, CancellationToken ct = default)
    {
        if (context.WorkspaceDir is null) return null;

        var path = Path.Combine(context.WorkspaceDir, "heartbeat.md");
        if (!File.Exists(path)) return null;

        return await File.ReadAllTextAsync(path, ct);
    }
}
