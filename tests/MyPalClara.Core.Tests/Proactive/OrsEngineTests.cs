using MyPalClara.Modules.Proactive.Engine;

namespace MyPalClara.Core.Tests.Proactive;

public class OrsEngineTests
{
    [Fact]
    public void OrsState_InitialState_IsWait()
    {
        Assert.Equal(OrsState.Wait, OrsState.Wait);
    }

    [Fact]
    public void OrsState_AllStatesExist()
    {
        var states = new[] { OrsState.Wait, OrsState.Think, OrsState.Speak };
        Assert.Equal(3, states.Distinct().Count());
    }

    [Fact]
    public void OrsContext_CanBeConstructed()
    {
        var ctx = new OrsContext
        {
            UserId = "user-1",
            CurrentState = OrsState.Wait
        };
        Assert.Equal("user-1", ctx.UserId);
        Assert.Equal(OrsState.Wait, ctx.CurrentState);
    }

    [Fact]
    public void OrsDecision_ParseFromString()
    {
        var decision = OrsDecision.Parse("WAIT");
        Assert.Equal(OrsState.Wait, decision.NextState);

        decision = OrsDecision.Parse("THINK");
        Assert.Equal(OrsState.Think, decision.NextState);

        decision = OrsDecision.Parse("SPEAK");
        Assert.Equal(OrsState.Speak, decision.NextState);
    }

    [Fact]
    public void OrsDecision_ParseInvalid_DefaultsToWait()
    {
        var decision = OrsDecision.Parse("INVALID");
        Assert.Equal(OrsState.Wait, decision.NextState);
    }
}
