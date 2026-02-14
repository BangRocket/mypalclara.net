using MyPalClara.Skills;

namespace MyPalClara.Skills.Tests;

public class SkillParserTests
{
    [Fact]
    public void Parse_ValidSkillMd_ExtractsAllFields()
    {
        var content = """
            ---
            name: code-review
            version: 1.0.0
            description: Perform a code review
            triggers:
              - pattern: "review (this|the) code"
            inputs:
              - name: target
                type: string
                required: true
            tools_required:
              - filesystem__read_file
            ---

            # Code Review Skill
            Review the following: {{ target }}
            """;

        var skill = SkillParser.Parse(content, "/skills/code-review.skill.md");

        Assert.Equal("code-review", skill.Name);
        Assert.Equal("1.0.0", skill.Version);
        Assert.Equal("Perform a code review", skill.Description);
        Assert.Single(skill.Triggers);
        Assert.Equal("review (this|the) code", skill.Triggers[0].Pattern);
        Assert.Single(skill.Inputs);
        Assert.Equal("target", skill.Inputs[0].Name);
        Assert.True(skill.Inputs[0].Required);
        Assert.Single(skill.ToolsRequired);
        Assert.Equal("filesystem__read_file", skill.ToolsRequired[0]);
        Assert.Contains("Code Review Skill", skill.PromptTemplate);
        Assert.Contains("{{ target }}", skill.PromptTemplate);
    }

    [Fact]
    public void Parse_MinimalSkillMd_UsesDefaults()
    {
        var content = """
            ---
            name: simple
            description: A simple skill
            ---

            Do the thing.
            """;

        var skill = SkillParser.Parse(content);

        Assert.Equal("simple", skill.Name);
        Assert.Equal("1.0.0", skill.Version);
        Assert.Empty(skill.Triggers);
        Assert.Empty(skill.Inputs);
        Assert.Empty(skill.ToolsRequired);
        Assert.Contains("Do the thing.", skill.PromptTemplate);
    }

    [Fact]
    public void Parse_MissingName_Throws()
    {
        var content = """
            ---
            description: Missing name
            ---

            Body.
            """;

        Assert.Throws<InvalidOperationException>(() => SkillParser.Parse(content));
    }

    [Theory]
    [InlineData("code-review")]
    [InlineData("commit-message")]
    [InlineData("daily-briefing")]
    [InlineData("debug-helper")]
    [InlineData("explain-code")]
    [InlineData("refactor")]
    [InlineData("research")]
    [InlineData("summarize")]
    [InlineData("translate")]
    [InlineData("write-tests")]
    public void Parse_BundledSkill_ParsesSuccessfully(string skillName)
    {
        var skillsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "skills"));
        var path = Path.Combine(skillsDir, $"{skillName}.skill.md");

        Assert.True(File.Exists(path), $"Bundled skill not found: {path}");

        var content = File.ReadAllText(path);
        var skill = SkillParser.Parse(content, path);

        Assert.Equal(skillName, skill.Name);
        Assert.NotEmpty(skill.Description);
        Assert.NotEmpty(skill.PromptTemplate);
    }
}
