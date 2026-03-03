namespace Clara.Gateway.Sandbox;

public interface ISandboxProvider
{
    Task<SandboxResult> ExecuteAsync(string code, string language, int timeoutSeconds = 30, CancellationToken ct = default);
}

public record SandboxResult(int ExitCode, string Stdout, string Stderr);
