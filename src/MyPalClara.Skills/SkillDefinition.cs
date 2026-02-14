namespace MyPalClara.Skills;

/// <summary>Parsed data model from a SKILL.md file.</summary>
public sealed class SkillDefinition
{
    public required string Name { get; init; }
    public string Version { get; init; } = "1.0.0";
    public required string Description { get; init; }
    public List<SkillTrigger> Triggers { get; init; } = [];
    public List<SkillInput> Inputs { get; init; } = [];
    public List<string> ToolsRequired { get; init; } = [];
    public required string PromptTemplate { get; init; }
    public string? FilePath { get; init; }
}

/// <summary>A regex pattern that activates a skill.</summary>
public sealed class SkillTrigger
{
    public required string Pattern { get; init; }
}

/// <summary>A named input parameter for a skill.</summary>
public sealed class SkillInput
{
    public required string Name { get; init; }
    public string Type { get; init; } = "string";
    public bool Required { get; init; }
    public string? Default { get; init; }
    public string? Description { get; init; }
}
