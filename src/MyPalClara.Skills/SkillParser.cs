using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MyPalClara.Skills;

/// <summary>
/// Parses SKILL.md files: YAML frontmatter (between --- markers) + Markdown body as prompt template.
/// </summary>
public static class SkillParser
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Parse a SKILL.md file into a <see cref="SkillDefinition"/>.</summary>
    public static SkillDefinition Parse(string content, string? filePath = null)
    {
        var (frontmatter, body) = SplitFrontmatter(content);
        var meta = YamlDeserializer.Deserialize<SkillFrontmatter>(frontmatter)
            ?? throw new InvalidOperationException($"Failed to parse YAML frontmatter in {filePath ?? "skill"}");

        return new SkillDefinition
        {
            Name = meta.Name ?? throw new InvalidOperationException("Skill 'name' is required in frontmatter"),
            Version = meta.Version ?? "1.0.0",
            Description = meta.Description
                ?? throw new InvalidOperationException("Skill 'description' is required in frontmatter"),
            Triggers = meta.Triggers?.Where(t => t.Pattern is not null).Select(t => new SkillTrigger { Pattern = t.Pattern! }).ToList() ?? [],
            Inputs = meta.Inputs?.Select(i => new SkillInput
            {
                Name = i.Name ?? throw new InvalidOperationException("Input 'name' is required"),
                Type = i.Type ?? "string",
                Required = i.Required,
                Default = i.Default,
                Description = i.Description,
            }).ToList() ?? [],
            ToolsRequired = meta.ToolsRequired ?? [],
            PromptTemplate = body.Trim(),
            FilePath = filePath,
        };
    }

    /// <summary>Parse a SKILL.md file from disk.</summary>
    public static async Task<SkillDefinition> ParseFileAsync(string path, CancellationToken ct = default)
    {
        var content = await File.ReadAllTextAsync(path, ct);
        return Parse(content, path);
    }

    private static (string Frontmatter, string Body) SplitFrontmatter(string content)
    {
        const string delimiter = "---";

        var text = content.TrimStart();
        if (!text.StartsWith(delimiter))
            throw new InvalidOperationException("SKILL.md must begin with '---' YAML frontmatter delimiter");

        var endIndex = text.IndexOf(delimiter, delimiter.Length, StringComparison.Ordinal);
        if (endIndex < 0)
            throw new InvalidOperationException("SKILL.md missing closing '---' YAML frontmatter delimiter");

        var frontmatter = text[delimiter.Length..endIndex].Trim();
        var body = text[(endIndex + delimiter.Length)..];
        return (frontmatter, body);
    }

    /// <summary>Internal YAML model for frontmatter deserialization.</summary>
    private sealed class SkillFrontmatter
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        public List<TriggerDef>? Triggers { get; set; }
        public List<InputDef>? Inputs { get; set; }
        public List<string>? ToolsRequired { get; set; }
    }

    private sealed class TriggerDef
    {
        public string? Pattern { get; set; }
    }

    private sealed class InputDef
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public bool Required { get; set; }
        public string? Default { get; set; }
        public string? Description { get; set; }
    }
}
