using System.Text;

namespace Clara.Core.Prompt;

/// <summary>
/// Composes a system prompt from multiple sections, ordered by priority.
/// </summary>
public class PromptComposer
{
    private readonly IEnumerable<IPromptSection> _sections;

    public PromptComposer(IEnumerable<IPromptSection> sections)
    {
        _sections = sections;
    }

    public async Task<string> ComposeAsync(PromptContext context, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var orderedSections = _sections.OrderBy(s => s.Priority);

        foreach (var section in orderedSections)
        {
            var content = await section.GetContentAsync(context, ct);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            sb.AppendLine($"<{section.Name}>");
            sb.AppendLine(content.Trim());
            sb.AppendLine($"</{section.Name}>");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
