using MyPalClara.Modules.Email.Models;
using MyPalClara.Modules.Email.Rules;

namespace MyPalClara.Core.Tests.Email;

public class RulesEngineTests
{
    [Fact]
    public void Evaluate_FromContains_Matches()
    {
        var rule = new Rule("r1", "Test", [new RuleCondition("from_contains", "boss@")], "all", "notify");
        var msg = new EmailMessage("1", "boss@company.com", "Hello", "Body", DateTime.UtcNow);

        Assert.True(RulesEngine.Evaluate(rule, msg));
    }

    [Fact]
    public void Evaluate_FromContains_NoMatch()
    {
        var rule = new Rule("r1", "Test", [new RuleCondition("from_contains", "boss@")], "all", "notify");
        var msg = new EmailMessage("1", "friend@example.com", "Hello", "Body", DateTime.UtcNow);

        Assert.False(RulesEngine.Evaluate(rule, msg));
    }

    [Fact]
    public void Evaluate_SubjectContains_Matches()
    {
        var rule = new Rule("r1", "Test", [new RuleCondition("subject_contains", "urgent")], "all", "notify");
        var msg = new EmailMessage("1", "a@b.com", "URGENT: fix now", "Body", DateTime.UtcNow);

        Assert.True(RulesEngine.Evaluate(rule, msg));
    }

    [Fact]
    public void Evaluate_AnyOperator_OneMatch()
    {
        var rule = new Rule("r1", "Test",
            [new RuleCondition("from_contains", "boss"), new RuleCondition("subject_contains", "xyz")],
            "any", "notify");
        var msg = new EmailMessage("1", "boss@co.com", "Hello", "Body", DateTime.UtcNow);

        Assert.True(RulesEngine.Evaluate(rule, msg));
    }

    [Fact]
    public void Evaluate_AllOperator_MustMatchAll()
    {
        var rule = new Rule("r1", "Test",
            [new RuleCondition("from_contains", "boss"), new RuleCondition("subject_contains", "urgent")],
            "all", "notify");
        var msg = new EmailMessage("1", "boss@co.com", "Hello", "Body", DateTime.UtcNow);

        Assert.False(RulesEngine.Evaluate(rule, msg)); // subject doesn't match
    }
}
