namespace MyPalClara.Modules.Sandbox.Models;

public record ExecutionResult(int ExitCode, string Stdout, string Stderr, bool Success);
