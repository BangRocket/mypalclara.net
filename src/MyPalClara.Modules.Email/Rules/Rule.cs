namespace MyPalClara.Modules.Email.Rules;

public record RuleCondition(string Type, string Value);

public record Rule(string Id, string Name, List<RuleCondition> Conditions, string Operator, string Action);
