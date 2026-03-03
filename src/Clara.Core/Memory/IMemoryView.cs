namespace Clara.Core.Memory;

public interface IMemoryView
{
    Task<string> ExportToMarkdownAsync(string userId, CancellationToken ct = default);
    Task ImportFromMarkdownAsync(string userId, string markdown, CancellationToken ct = default);
    Task<string?> GetReadableAsync(string userId, Guid memoryId, CancellationToken ct = default);
}
