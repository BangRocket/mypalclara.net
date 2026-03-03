using Clara.Core.Memory;

namespace Clara.Core.Prompt;

/// <summary>
/// Searches memory store for relevant context and formats results.
/// </summary>
public class MemorySection : IPromptSection
{
    private readonly IMemoryStore _memoryStore;
    private readonly int _topK;

    public string Name => "memories";
    public int Priority => 300;

    public MemorySection(IMemoryStore memoryStore, int topK = 10)
    {
        _memoryStore = memoryStore;
        _topK = topK;
    }

    public async Task<string?> GetContentAsync(PromptContext context, CancellationToken ct = default)
    {
        var memories = await _memoryStore.GetAllAsync(context.UserId, ct);

        if (memories.Count == 0)
            return null;

        // Take top-K by score, then format
        var topMemories = memories
            .OrderByDescending(m => m.Score)
            .Take(_topK);

        var lines = topMemories.Select(m =>
        {
            var prefix = m.Category is not null ? $"[{m.Category}] " : "";
            return $"- {prefix}{m.Content}";
        });

        return string.Join("\n", lines);
    }
}
