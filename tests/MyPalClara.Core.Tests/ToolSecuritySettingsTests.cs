using MyPalClara.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace MyPalClara.Core.Tests;

public class ToolSecuritySettingsTests
{
    [Fact]
    public void Defaults_AllowMode_LogEnabled()
    {
        var settings = new ToolSecuritySettings();

        Assert.Equal(ToolApprovalMode.Allow, settings.DefaultMode);
        Assert.True(settings.LogAllCalls);
        Assert.Equal(120, settings.MaxExecutionSeconds);
        Assert.Empty(settings.AllowList);
        Assert.Empty(settings.BlockList);
        Assert.Empty(settings.ApprovalRequired);
    }

    [Fact]
    public void ConfigBinding_RoundTrip()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ToolSecurity:DefaultMode"] = "Block",
                ["ToolSecurity:MaxExecutionSeconds"] = "60",
                ["ToolSecurity:LogAllCalls"] = "false",
                ["ToolSecurity:BlockList:0"] = "shell__*",
                ["ToolSecurity:BlockList:1"] = "file__delete",
                ["ToolSecurity:AllowList:0"] = "memory__*",
            })
            .Build();

        var result = ConfigLoader.Bind(config);

        Assert.Equal(ToolApprovalMode.Block, result.ToolSecurity.DefaultMode);
        Assert.Equal(60, result.ToolSecurity.MaxExecutionSeconds);
        Assert.False(result.ToolSecurity.LogAllCalls);
        Assert.Equal(["shell__*", "file__delete"], result.ToolSecurity.BlockList);
        Assert.Equal(["memory__*"], result.ToolSecurity.AllowList);
    }

    [Fact]
    public void ClaraConfig_HasDefaultToolSecurity()
    {
        var config = new ClaraConfig();

        Assert.NotNull(config.ToolSecurity);
        Assert.Equal(ToolApprovalMode.Allow, config.ToolSecurity.DefaultMode);
    }

    [Fact]
    public void ClaraConfig_HasDefaultScheduler()
    {
        var config = new ClaraConfig();

        Assert.NotNull(config.Scheduler);
        Assert.False(config.Scheduler.Enabled);
        Assert.Empty(config.Scheduler.Jobs);
    }

    [Fact]
    public void ClaraConfig_HasDefaultAgentSettings()
    {
        var config = new ClaraConfig();

        Assert.NotNull(config.Agents);
        Assert.False(config.Agents.MultiAgentEnabled);
        Assert.Empty(config.Agents.Profiles);
        Assert.Empty(config.Agents.ChannelRouting);
    }

    [Fact]
    public void ClaraConfig_HasDefaultSignalSettings()
    {
        var config = new ClaraConfig();

        Assert.NotNull(config.Signal);
        Assert.Equal("signal-cli", config.Signal.SignalCliPath);
        Assert.Empty(config.Signal.AccountPhone);
        Assert.Equal(4096, config.Signal.MaxMessageLength);
    }
}
