using System.Collections.Concurrent;

namespace Clara.Core.Tools;

public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new();

    public void Register(ITool tool) => _tools[tool.Name] = tool;

    public ITool? Resolve(string name) => _tools.GetValueOrDefault(name);

    public IReadOnlyList<ITool> GetByCategory(ToolCategory category) =>
        _tools.Values.Where(t => t.Category == category).ToList();

    public IReadOnlyList<ITool> GetAll() => _tools.Values.ToList();
}
