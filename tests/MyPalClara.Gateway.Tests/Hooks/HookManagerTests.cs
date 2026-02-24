using Microsoft.Extensions.Logging.Abstractions;
using MyPalClara.Gateway.Events;
using MyPalClara.Gateway.Hooks;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tests.Hooks;

public class HookManagerTests
{
    private readonly EventBus _eventBus = new();
    private readonly HookManager _manager;

    public HookManagerTests()
    {
        _manager = new HookManager(_eventBus, NullLogger<HookManager>.Instance);
    }

    [Fact]
    public async Task Register_ShellHook_FiresAndTracksResult()
    {
        // Arrange
        var hook = new Hook
        {
            Name = "test-echo",
            Event = "test.fired",
            Type = HookType.Shell,
            Command = "echo hello"
        };

        _manager.Register(hook);

        // Act
        await _eventBus.PublishAsync(new GatewayEvent("test.fired", DateTime.UtcNow));

        // Allow a moment for async handler to complete
        await Task.Delay(200);

        // Assert
        var results = _manager.GetResults();
        Assert.Single(results);
        Assert.Equal("test-echo", results[0].HookName);
        Assert.Equal("test.fired", results[0].EventType);
        Assert.True(results[0].Success);
        Assert.Equal("hello", results[0].Output);
    }

    [Fact]
    public async Task DisabledHook_DoesNotExecute()
    {
        // Arrange
        var hook = new Hook
        {
            Name = "disabled-hook",
            Event = "test.disabled",
            Type = HookType.Shell,
            Command = "echo should-not-run"
        };

        _manager.Register(hook);
        _manager.Disable("disabled-hook");

        // Act
        await _eventBus.PublishAsync(new GatewayEvent("test.disabled", DateTime.UtcNow));
        await Task.Delay(200);

        // Assert
        var results = _manager.GetResults();
        Assert.Empty(results);
    }

    [Fact]
    public async Task EnableHook_ReenablesExecution()
    {
        // Arrange
        var hook = new Hook
        {
            Name = "toggle-hook",
            Event = "test.toggle",
            Type = HookType.Shell,
            Command = "echo toggled"
        };

        _manager.Register(hook);
        _manager.Disable("toggle-hook");
        _manager.Enable("toggle-hook");

        // Act
        await _eventBus.PublishAsync(new GatewayEvent("test.toggle", DateTime.UtcNow));
        await Task.Delay(200);

        // Assert
        var results = _manager.GetResults();
        Assert.Single(results);
        Assert.True(results[0].Success);
    }

    [Fact]
    public void LoadFromFile_LoadsHooksFromYaml()
    {
        // Arrange
        var yaml = """
            hooks:
              - name: yaml-hook-1
                event: gateway.started
                command: echo started
                timeout: 10.0
                enabled: true
                priority: 5
              - name: yaml-hook-2
                event: gateway.stopped
                command: echo stopped
            """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, yaml);

            // Act
            _manager.LoadFromFile(tempFile);

            // Assert
            var hooks = _manager.GetHooks();
            Assert.Equal(2, hooks.Count);
            Assert.Equal("yaml-hook-1", hooks[0].Name);
            Assert.Equal("gateway.started", hooks[0].Event);
            Assert.Equal("echo started", hooks[0].Command);
            Assert.Equal(10.0, hooks[0].Timeout);
            Assert.Equal(5, hooks[0].Priority);
            Assert.Equal("yaml-hook-2", hooks[1].Name);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromFile_NonexistentFile_DoesNotThrow()
    {
        // Act & Assert — should not throw
        _manager.LoadFromFile("/nonexistent/path/hooks.yaml");
        Assert.Empty(_manager.GetHooks());
    }

    [Fact]
    public async Task GetStats_ReturnsCorrectCounts()
    {
        // Arrange — two hooks, one will succeed, one will fail
        _manager.Register(new Hook
        {
            Name = "pass-hook",
            Event = "test.stats",
            Type = HookType.Shell,
            Command = "echo pass"
        });

        _manager.Register(new Hook
        {
            Name = "fail-hook",
            Event = "test.stats",
            Type = HookType.Shell,
            Command = "exit 1"
        });

        // A third hook that is disabled
        _manager.Register(new Hook
        {
            Name = "disabled-stats",
            Event = "test.stats",
            Type = HookType.Shell,
            Command = "echo nope",
            Enabled = false
        });

        // Act
        await _eventBus.PublishAsync(new GatewayEvent("test.stats", DateTime.UtcNow));
        await Task.Delay(500);

        // Assert
        var (total, enabled, successes, failures) = _manager.GetStats();
        Assert.Equal(3, total);
        Assert.Equal(2, enabled);
        Assert.Equal(1, successes);
        Assert.Equal(1, failures);
    }

    [Fact]
    public void Unregister_RemovesHookFromList()
    {
        // Arrange
        _manager.Register(new Hook
        {
            Name = "to-remove",
            Event = "test.remove",
            Type = HookType.Shell,
            Command = "echo remove-me"
        });

        _manager.Register(new Hook
        {
            Name = "to-keep",
            Event = "test.remove",
            Type = HookType.Shell,
            Command = "echo keep-me"
        });

        // Act
        _manager.Unregister("to-remove");

        // Assert
        var hooks = _manager.GetHooks();
        Assert.Single(hooks);
        Assert.Equal("to-keep", hooks[0].Name);
    }

    [Fact]
    public async Task CodeHook_ExecutesHandler()
    {
        // Arrange
        var executed = false;

        _manager.Register(new Hook
        {
            Name = "code-hook",
            Event = "test.code",
            Type = HookType.Code,
            Handler = _ =>
            {
                executed = true;
                return Task.CompletedTask;
            }
        });

        // Act
        await _eventBus.PublishAsync(new GatewayEvent("test.code", DateTime.UtcNow));
        await Task.Delay(200);

        // Assert
        Assert.True(executed);
        var results = _manager.GetResults();
        Assert.Single(results);
        Assert.True(results[0].Success);
    }

    [Fact]
    public async Task CodeHook_FailingHandler_TracksError()
    {
        // Arrange
        _manager.Register(new Hook
        {
            Name = "failing-code",
            Event = "test.fail",
            Type = HookType.Code,
            Handler = _ => throw new InvalidOperationException("deliberate failure")
        });

        // Act
        await _eventBus.PublishAsync(new GatewayEvent("test.fail", DateTime.UtcNow));
        await Task.Delay(200);

        // Assert
        var results = _manager.GetResults();
        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Equal("deliberate failure", results[0].Error);
    }
}
