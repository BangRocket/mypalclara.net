using Clara.Gateway.Sandbox;

namespace Clara.Gateway.Tests.Sandbox;

public class SandboxManagerTests
{
    [Fact]
    public async Task ExecuteCodeAsync_delegates_to_provider()
    {
        var expected = new SandboxResult(0, "output", "");
        var provider = new MockSandboxProvider(expected);
        var manager = new SandboxManager(provider);

        var result = await manager.ExecuteCodeAsync("print('hi')", "python", 10);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("output", result.Stdout);
        Assert.Equal("", result.Stderr);
    }

    [Fact]
    public async Task ExecuteCodeAsync_returns_failure_from_provider()
    {
        var expected = new SandboxResult(1, "", "SyntaxError");
        var provider = new MockSandboxProvider(expected);
        var manager = new SandboxManager(provider);

        var result = await manager.ExecuteCodeAsync("invalid code", "python");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("SyntaxError", result.Stderr);
    }

    [Fact]
    public async Task ExecuteCodeAsync_returns_timeout_from_provider()
    {
        var expected = new SandboxResult(-1, "", "Execution timed out");
        var provider = new MockSandboxProvider(expected);
        var manager = new SandboxManager(provider);

        var result = await manager.ExecuteCodeAsync("while True: pass", "python", 5);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("timed out", result.Stderr);
    }

    [Fact]
    public async Task ExecuteCodeAsync_passes_parameters_to_provider()
    {
        var provider = new CapturingProvider();
        var manager = new SandboxManager(provider);

        await manager.ExecuteCodeAsync("code here", "javascript", 42);

        Assert.Equal("code here", provider.LastCode);
        Assert.Equal("javascript", provider.LastLanguage);
        Assert.Equal(42, provider.LastTimeout);
    }

    private class MockSandboxProvider : ISandboxProvider
    {
        private readonly SandboxResult _result;
        public MockSandboxProvider(SandboxResult result) => _result = result;

        public Task<SandboxResult> ExecuteAsync(string code, string language, int timeoutSeconds = 30, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private class CapturingProvider : ISandboxProvider
    {
        public string? LastCode { get; private set; }
        public string? LastLanguage { get; private set; }
        public int LastTimeout { get; private set; }

        public Task<SandboxResult> ExecuteAsync(string code, string language, int timeoutSeconds = 30, CancellationToken ct = default)
        {
            LastCode = code;
            LastLanguage = language;
            LastTimeout = timeoutSeconds;
            return Task.FromResult(new SandboxResult(0, "", ""));
        }
    }
}
