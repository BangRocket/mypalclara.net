namespace MyPalClara.Core.Configuration;

public sealed class SkillsSettings
{
    public string Directory { get; set; } = "~/.mypalclara/skills";
    public bool Enabled { get; set; } = true;
}
