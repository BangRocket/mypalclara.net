namespace Clara.Core.Prompt;

public interface IPromptSection
{
    string Name { get; }
    int Priority { get; }
    Task<string?> GetContentAsync(PromptContext context, CancellationToken ct = default);
}

public record PromptContext(string SessionKey, string UserId, string Platform, string? WorkspaceDir);
