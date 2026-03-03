namespace Clara.Core.Tools;

public class ToolSelector
{
    private static readonly Dictionary<string, ToolCategory[]> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["file"] = [ToolCategory.FileSystem],
        ["read"] = [ToolCategory.FileSystem],
        ["write"] = [ToolCategory.FileSystem],
        ["directory"] = [ToolCategory.FileSystem],
        ["run"] = [ToolCategory.Shell, ToolCategory.CodeExecution],
        ["execute"] = [ToolCategory.Shell, ToolCategory.CodeExecution],
        ["command"] = [ToolCategory.Shell],
        ["search"] = [ToolCategory.Web],
        ["web"] = [ToolCategory.Web],
        ["browse"] = [ToolCategory.Web],
        ["github"] = [ToolCategory.GitHub],
        ["repo"] = [ToolCategory.GitHub],
        ["pull request"] = [ToolCategory.GitHub],
        ["pr"] = [ToolCategory.GitHub],
        ["issue"] = [ToolCategory.GitHub],
        ["email"] = [ToolCategory.Email],
        ["memory"] = [ToolCategory.Memory],
        ["remember"] = [ToolCategory.Memory],
        ["forget"] = [ToolCategory.Memory],
        ["session"] = [ToolCategory.Session],
        ["code"] = [ToolCategory.CodeExecution],
        ["python"] = [ToolCategory.CodeExecution],
    };

    /// <summary>
    /// Returns relevant categories based on keyword matching,
    /// or all categories if confidence is low (no keywords matched).
    /// </summary>
    public IReadOnlyList<ToolCategory> SelectCategories(string messageContent)
    {
        var categories = new HashSet<ToolCategory>();
        foreach (var (keyword, cats) in Keywords)
        {
            if (messageContent.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var c in cats) categories.Add(c);
            }
        }

        // Low confidence → return all categories
        if (categories.Count == 0)
            return Enum.GetValues<ToolCategory>();

        return categories.ToList();
    }
}
