using System.Text.Json;
using MyPalClara.Core.Llm;

namespace MyPalClara.Skills;

/// <summary>
/// Exposes loaded skills as LLM-callable tools by generating ToolSchema objects.
/// Tool names are prefixed with "skill__" to avoid collisions with MCP tools.
/// </summary>
public sealed class SkillToolProvider
{
    private readonly SkillRegistry _registry;

    public SkillToolProvider(SkillRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Generates ToolSchema objects for all loaded skills.</summary>
    public List<ToolSchema> GetToolSchemas()
    {
        return _registry.GetAll()
            .Select(skill => new ToolSchema(
                Name: $"skill__{skill.Name}",
                Description: skill.Description,
                InputSchema: BuildInputSchema(skill)))
            .ToList();
    }

    /// <summary>Checks whether a tool name belongs to a skill.</summary>
    public static bool IsSkillTool(string toolName) => toolName.StartsWith("skill__");

    /// <summary>Extracts the skill name from a skill tool name.</summary>
    public static string GetSkillName(string toolName) =>
        toolName.StartsWith("skill__") ? toolName["skill__".Length..] : toolName;

    private static JsonElement BuildInputSchema(SkillDefinition skill)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        // Always include a "message" property for the user's free-form input
        properties["message"] = new Dictionary<string, object>
        {
            ["type"] = "string",
            ["description"] = "The user's message or request for this skill",
        };
        required.Add("message");

        foreach (var input in skill.Inputs)
        {
            var prop = new Dictionary<string, object>
            {
                ["type"] = MapType(input.Type),
            };

            if (input.Description is not null)
                prop["description"] = input.Description;

            if (input.Default is not null)
                prop["default"] = input.Default;

            properties[input.Name] = prop;

            if (input.Required)
                required.Add(input.Name);
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
        };

        // Round-trip through JSON to get a JsonElement
        var json = JsonSerializer.Serialize(schema);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static string MapType(string skillType) => skillType.ToLowerInvariant() switch
    {
        "string" => "string",
        "int" or "integer" => "integer",
        "float" or "double" or "number" => "number",
        "bool" or "boolean" => "boolean",
        _ => "string",
    };
}
