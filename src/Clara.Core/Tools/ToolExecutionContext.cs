namespace Clara.Core.Tools;

public record ToolExecutionContext(
    string UserId,
    string SessionKey,
    string Platform,
    bool IsSandboxed,
    string? WorkspaceDir);
