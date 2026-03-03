using Clara.Gateway.Hooks;

namespace Clara.Gateway.Tests.Hooks;

public class HookRegistryTests
{
    [Fact]
    public void LoadFromYamlString_loads_hooks()
    {
        var registry = new HookRegistry();
        registry.LoadFromYamlString("""
            hooks:
              - name: on-message
                event: message:received
                command: echo hello
                priority: 0
              - name: on-start
                event: gateway:startup
                command: echo started
                priority: 1
            """);

        var all = registry.GetAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void GetHooksForEvent_filters_by_event_type()
    {
        var registry = new HookRegistry();
        registry.LoadFromYamlString("""
            hooks:
              - name: msg-hook
                event: message:received
                command: echo msg
              - name: start-hook
                event: gateway:startup
                command: echo start
              - name: msg-hook-2
                event: message:received
                command: echo msg2
            """);

        var msgHooks = registry.GetHooksForEvent("message:received");
        Assert.Equal(2, msgHooks.Count);
        Assert.All(msgHooks, h => Assert.Equal("message:received", h.Event));

        var startHooks = registry.GetHooksForEvent("gateway:startup");
        Assert.Single(startHooks);
    }

    [Fact]
    public void GetHooksForEvent_returns_empty_for_unknown_event()
    {
        var registry = new HookRegistry();
        registry.LoadFromYamlString("""
            hooks:
              - name: test
                event: message:received
                command: echo test
            """);

        var hooks = registry.GetHooksForEvent("nonexistent:event");
        Assert.Empty(hooks);
    }

    [Fact]
    public void Disabled_hooks_are_skipped()
    {
        var registry = new HookRegistry();
        registry.LoadFromYamlString("""
            hooks:
              - name: enabled-hook
                event: message:received
                command: echo enabled
                enabled: true
              - name: disabled-hook
                event: message:received
                command: echo disabled
                enabled: false
            """);

        var hooks = registry.GetHooksForEvent("message:received");
        Assert.Single(hooks);
        Assert.Equal("enabled-hook", hooks[0].Name);
    }

    [Fact]
    public void Hooks_are_ordered_by_priority()
    {
        var registry = new HookRegistry();
        registry.LoadFromYamlString("""
            hooks:
              - name: low-priority
                event: message:received
                command: echo low
                priority: 10
              - name: high-priority
                event: message:received
                command: echo high
                priority: 1
              - name: mid-priority
                event: message:received
                command: echo mid
                priority: 5
            """);

        var hooks = registry.GetHooksForEvent("message:received");
        Assert.Equal("high-priority", hooks[0].Name);
        Assert.Equal("mid-priority", hooks[1].Name);
        Assert.Equal("low-priority", hooks[2].Name);
    }

    [Fact]
    public void LoadFromYaml_handles_missing_file()
    {
        var registry = new HookRegistry();
        registry.LoadFromYaml("/nonexistent/path/hooks.yaml");

        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public void Hook_defaults_are_correct()
    {
        var registry = new HookRegistry();
        registry.LoadFromYamlString("""
            hooks:
              - name: defaults-test
                event: test:event
                command: echo test
            """);

        var hook = registry.GetHooksForEvent("test:event")[0];
        Assert.Equal(30, hook.TimeoutSeconds);
        Assert.Null(hook.WorkingDir);
        Assert.Equal(0, hook.Priority);
    }
}
