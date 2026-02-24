using MyPalClara.Modules.Sandbox.Models;

namespace MyPalClara.Modules.Sandbox;

public interface ISandboxManager
{
    Task<ExecutionResult> ExecuteAsync(string userId, string command,
        string? workingDir = null, int? timeoutSeconds = null, CancellationToken ct = default);
    Task<ExecutionResult> ExecutePythonAsync(string userId, string code,
        int? timeoutSeconds = null, CancellationToken ct = default);
    Task<string> WriteFileAsync(string userId, string path, string content, CancellationToken ct = default);
    Task<string> ReadFileAsync(string userId, string path, CancellationToken ct = default);
    Task<string[]> ListFilesAsync(string userId, string path, CancellationToken ct = default);
}
