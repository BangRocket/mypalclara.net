using Clara.Core.Sessions;

namespace Clara.Core.Tests.Sessions;

public class SessionKeyTests
{
    [Fact]
    public void Parse_StandardKey()
    {
        var key = SessionKey.Parse("clara:main:discord:dm:12345");

        Assert.Equal("main", key.AgentId);
        Assert.Equal("discord", key.Platform);
        Assert.Equal("dm", key.Scope);
        Assert.Equal("12345", key.Identifier);
        Assert.Null(key.SubTaskId);
        Assert.False(key.IsSubAgent);
    }

    [Fact]
    public void Parse_SubAgentKey()
    {
        var key = SessionKey.Parse("clara:sub:main:discord:dm:12345:task1");

        Assert.Equal("main", key.AgentId);
        Assert.Equal("discord", key.Platform);
        Assert.Equal("dm", key.Scope);
        Assert.Equal("12345", key.Identifier);
        Assert.Equal("task1", key.SubTaskId);
        Assert.True(key.IsSubAgent);
    }

    [Fact]
    public void RoundTrip_StandardKey()
    {
        var original = new SessionKey
        {
            AgentId = "main",
            Platform = "discord",
            Scope = "dm",
            Identifier = "12345"
        };

        var parsed = SessionKey.Parse(original.ToString());

        Assert.Equal(original.AgentId, parsed.AgentId);
        Assert.Equal(original.Platform, parsed.Platform);
        Assert.Equal(original.Scope, parsed.Scope);
        Assert.Equal(original.Identifier, parsed.Identifier);
    }

    [Fact]
    public void RoundTrip_SubAgentKey()
    {
        var original = new SessionKey
        {
            AgentId = "main",
            Platform = "discord",
            Scope = "dm",
            Identifier = "12345",
            SubTaskId = "task1"
        };

        var parsed = SessionKey.Parse(original.ToString());

        Assert.Equal(original.SubTaskId, parsed.SubTaskId);
        Assert.True(parsed.IsSubAgent);
    }

    [Fact]
    public void ToString_StandardFormat()
    {
        var key = new SessionKey
        {
            AgentId = "main",
            Platform = "discord",
            Scope = "dm",
            Identifier = "12345"
        };

        Assert.Equal("clara:main:discord:dm:12345", key.ToString());
    }

    [Fact]
    public void ToString_SubAgentFormat()
    {
        var key = new SessionKey
        {
            AgentId = "main",
            Platform = "discord",
            Scope = "dm",
            Identifier = "12345",
            SubTaskId = "task1"
        };

        Assert.Equal("clara:sub:main:discord:dm:12345:task1", key.ToString());
    }

    [Fact]
    public void ParentKey_ReturnsWithoutSubTask()
    {
        var key = new SessionKey
        {
            AgentId = "main",
            Platform = "discord",
            Scope = "dm",
            Identifier = "12345",
            SubTaskId = "task1"
        };

        Assert.Equal("clara:main:discord:dm:12345", key.ParentKey);
    }

    [Fact]
    public void LaneKey_SubAgent_ReturnsFull()
    {
        var key = new SessionKey
        {
            AgentId = "main",
            Platform = "discord",
            Scope = "dm",
            Identifier = "12345",
            SubTaskId = "task1"
        };

        Assert.Equal("clara:sub:main:discord:dm:12345:task1", key.LaneKey);
    }

    [Fact]
    public void LaneKey_Standard_ReturnsParent()
    {
        var key = new SessionKey
        {
            AgentId = "main",
            Platform = "discord",
            Scope = "dm",
            Identifier = "12345"
        };

        Assert.Equal("clara:main:discord:dm:12345", key.LaneKey);
    }

    [Fact]
    public void Parse_InvalidKey_Throws()
    {
        Assert.Throws<FormatException>(() => SessionKey.Parse("invalid"));
        Assert.Throws<FormatException>(() => SessionKey.Parse("foo:bar"));
    }
}
