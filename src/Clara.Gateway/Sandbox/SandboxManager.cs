namespace Clara.Gateway.Sandbox;

public class SandboxManager
{
    private readonly ISandboxProvider _provider;

    public SandboxManager(ISandboxProvider provider) => _provider = provider;

    public Task<SandboxResult> ExecuteCodeAsync(
        string code, string language, int timeoutSeconds = 30, CancellationToken ct = default)
        => _provider.ExecuteAsync(code, language, timeoutSeconds, ct);
}
