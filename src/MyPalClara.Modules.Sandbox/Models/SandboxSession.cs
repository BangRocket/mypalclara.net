namespace MyPalClara.Modules.Sandbox.Models;

public record SandboxSession(string UserId, string ContainerId, DateTime CreatedAt, DateTime LastUsedAt);
