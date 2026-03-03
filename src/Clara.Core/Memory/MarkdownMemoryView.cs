using System.Text;
using System.Text.RegularExpressions;

namespace Clara.Core.Memory;

public partial class MarkdownMemoryView : IMemoryView
{
    private readonly IMemoryStore _store;

    public MarkdownMemoryView(IMemoryStore store)
    {
        _store = store;
    }

    public async Task<string> ExportToMarkdownAsync(string userId, CancellationToken ct = default)
    {
        var memories = await _store.GetAllAsync(userId, ct);

        if (memories.Count == 0)
            return "# Memories\n\nNo memories stored.\n";

        var sb = new StringBuilder();
        sb.AppendLine("# Memories");
        sb.AppendLine();

        // Group by category
        var grouped = memories
            .GroupBy(m => m.Category ?? "Uncategorized")
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();

            foreach (var memory in group.OrderByDescending(m => m.UpdatedAt))
            {
                sb.AppendLine($"- {memory.Content}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task ImportFromMarkdownAsync(string userId, string markdown, CancellationToken ct = default)
    {
        string? currentCategory = null;

        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();

            // Category header (## ...)
            if (trimmed.StartsWith("## "))
            {
                currentCategory = trimmed[3..].Trim();
                if (currentCategory == "Uncategorized")
                    currentCategory = null;
                continue;
            }

            // Memory item (- ...)
            if (trimmed.StartsWith("- "))
            {
                var content = trimmed[2..].Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    await _store.StoreAsync(userId, content,
                        new MemoryMetadata(Category: currentCategory), ct);
                }
            }
        }
    }

    public async Task<string?> GetReadableAsync(string userId, Guid memoryId, CancellationToken ct = default)
    {
        var memories = await _store.GetAllAsync(userId, ct);
        var memory = memories.FirstOrDefault(m => m.Id == memoryId);

        if (memory is null)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine($"**Memory** (ID: `{memory.Id}`)");
        sb.AppendLine();

        if (memory.Category is not null)
            sb.AppendLine($"**Category:** {memory.Category}");

        sb.AppendLine($"**Content:** {memory.Content}");
        sb.AppendLine($"**Score:** {memory.Score:F2}");
        sb.AppendLine($"**Created:** {memory.CreatedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"**Updated:** {memory.UpdatedAt:yyyy-MM-dd HH:mm}");

        return sb.ToString();
    }
}
