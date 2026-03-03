namespace Clara.Core.Tools;

public interface IToolRegistry
{
    void Register(ITool tool);
    ITool? Resolve(string name);
    IReadOnlyList<ITool> GetByCategory(ToolCategory category);
    IReadOnlyList<ITool> GetAll();
}
