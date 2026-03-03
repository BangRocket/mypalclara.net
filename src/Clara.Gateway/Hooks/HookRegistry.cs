using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Clara.Gateway.Hooks;

public class HookRegistry
{
    private readonly List<HookDefinition> _hooks = [];

    public void LoadFromYaml(string yamlPath)
    {
        if (!File.Exists(yamlPath)) return;
        var yaml = File.ReadAllText(yamlPath);
        LoadFromYamlString(yaml);
    }

    public void LoadFromYamlString(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var config = deserializer.Deserialize<HookConfig>(yaml);
        if (config?.Hooks != null)
            _hooks.AddRange(config.Hooks.Where(h => h.Enabled));
    }

    public IReadOnlyList<HookDefinition> GetHooksForEvent(string eventType) =>
        _hooks.Where(h => h.Event == eventType).OrderBy(h => h.Priority).ToList();

    public IReadOnlyList<HookDefinition> GetAll() => _hooks.ToList();
}

internal class HookConfig
{
    public List<HookDefinition>? Hooks { get; set; }
}
