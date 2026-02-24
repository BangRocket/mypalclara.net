using Microsoft.Extensions.Logging;
using MyPalClara.Modules.Sdk;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MyPalClara.Gateway.Hooks;

public class HookManager
{
    private readonly List<Hook> _hooks = [];
    private readonly List<HookResult> _results = [];
    private readonly IEventBus _eventBus;
    private readonly ILogger<HookManager> _logger;
    private readonly object _lock = new();
    private const int MaxResults = 100;

    public HookManager(IEventBus eventBus, ILogger<HookManager> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public void Register(Hook hook)
    {
        lock (_lock) { _hooks.Add(hook); }

        if (hook.Type == HookType.Shell && hook.Command != null)
        {
            _eventBus.Subscribe(hook.Event, async evt =>
            {
                if (!hook.Enabled) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (success, output, error) = await ShellExecutor.ExecuteAsync(
                    hook.Command, evt, hook.WorkingDir, hook.Timeout);
                sw.Stop();

                var result = new HookResult(hook.Name, evt.Type, success, output, error, DateTime.UtcNow, sw.Elapsed);
                AddResult(result);

                if (!success)
                    _logger.LogWarning("Hook {Name} failed: {Error}", hook.Name, error);
            }, hook.Priority);
        }
        else if (hook.Type == HookType.Code && hook.Handler != null)
        {
            var handler = hook.Handler;
            _eventBus.Subscribe(hook.Event, async evt =>
            {
                if (!hook.Enabled) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await handler(evt);
                    sw.Stop();
                    AddResult(new HookResult(hook.Name, evt.Type, true, null, null, DateTime.UtcNow, sw.Elapsed));
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    AddResult(new HookResult(hook.Name, evt.Type, false, null, ex.Message, DateTime.UtcNow, sw.Elapsed));
                    _logger.LogWarning(ex, "Code hook {Name} failed", hook.Name);
                }
            }, hook.Priority);
        }
    }

    public void Unregister(string name)
    {
        lock (_lock) { _hooks.RemoveAll(h => h.Name == name); }
    }

    public void Enable(string name)
    {
        lock (_lock) { var h = _hooks.Find(h => h.Name == name); if (h != null) h.Enabled = true; }
    }

    public void Disable(string name)
    {
        lock (_lock) { var h = _hooks.Find(h => h.Name == name); if (h != null) h.Enabled = false; }
    }

    public IReadOnlyList<Hook> GetHooks()
    {
        lock (_lock) { return [.. _hooks]; }
    }

    public IReadOnlyList<HookResult> GetResults(int limit = 100)
    {
        lock (_lock) { return _results.TakeLast(Math.Min(limit, _results.Count)).ToList(); }
    }

    public (int Total, int Enabled, int Successes, int Failures) GetStats()
    {
        lock (_lock)
        {
            return (
                _hooks.Count,
                _hooks.Count(h => h.Enabled),
                _results.Count(r => r.Success),
                _results.Count(r => !r.Success));
        }
    }

    public void LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            _logger.LogDebug("No hooks file at {Path}", path);
            return;
        }

        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var config = deserializer.Deserialize<HooksConfig>(yaml);
        if (config?.Hooks == null) return;

        foreach (var entry in config.Hooks)
        {
            Register(new Hook
            {
                Name = entry.Name,
                Event = entry.Event,
                Type = HookType.Shell,
                Command = entry.Command,
                Timeout = entry.Timeout,
                WorkingDir = entry.WorkingDir,
                Enabled = entry.Enabled,
                Priority = entry.Priority
            });
        }

        _logger.LogInformation("Loaded {Count} hooks from {Path}", config.Hooks.Count, path);
    }

    private void AddResult(HookResult result)
    {
        lock (_lock)
        {
            _results.Add(result);
            if (_results.Count > MaxResults)
                _results.RemoveAt(0);
        }
    }
}

// YAML deserialization models
internal class HooksConfig
{
    public List<HookEntry> Hooks { get; set; } = [];
}

internal class HookEntry
{
    public string Name { get; set; } = "";
    public string Event { get; set; } = "";
    public string Command { get; set; } = "";
    public double Timeout { get; set; } = 30.0;
    public string? WorkingDir { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
}
