using MyPalClara.Modules.Email.Models;

namespace MyPalClara.Modules.Email.Rules;

public static class RulesEngine
{
    public static bool Evaluate(Rule rule, EmailMessage message)
    {
        if (rule.Conditions.Count == 0) return false;

        var results = rule.Conditions.Select(c => EvaluateCondition(c, message));

        return rule.Operator.ToLowerInvariant() switch
        {
            "any" => results.Any(r => r),
            "all" => results.All(r => r),
            _ => results.All(r => r)
        };
    }

    private static bool EvaluateCondition(RuleCondition condition, EmailMessage message)
    {
        return condition.Type.ToLowerInvariant() switch
        {
            "from_contains" => message.From.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            "from_exact" => message.From.Equals(condition.Value, StringComparison.OrdinalIgnoreCase),
            "subject_contains" => message.Subject.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            "body_contains" => message.Body.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            "has_attachment" => message.HasAttachment == bool.Parse(condition.Value),
            _ => false
        };
    }
}
