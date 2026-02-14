using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Skills;

/// <summary>
/// Scans a directory for *.skill.md files, parses them, and provides lookup and trigger matching.
/// </summary>
public sealed class SkillRegistry
{
    private readonly ConcurrentDictionary<string, SkillDefinition> _skills = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Regex> _triggerCache = new();
    private readonly ILogger<SkillRegistry> _logger;

    public SkillRegistry(ILogger<SkillRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>Scan a directory for *.skill.md files and parse them all.</summary>
    public async Task LoadSkillsAsync(string directory, CancellationToken ct = default)
    {
        // Load bundled skills from the executable's directory first
        var bundledDir = Path.Combine(AppContext.BaseDirectory, "skills");
        if (Directory.Exists(bundledDir))
            await ScanDirectoryAsync(bundledDir, ct);

        // Then load user skills (can override bundled by name)
        var expanded = ExpandPath(directory);
        if (Directory.Exists(expanded))
            await ScanDirectoryAsync(expanded, ct);
        else
            _logger.LogDebug("User skills directory does not exist: {Directory}", expanded);
    }

    private async Task ScanDirectoryAsync(string directory, CancellationToken ct)
    {
        var files = Directory.GetFiles(directory, "*.skill.md", SearchOption.AllDirectories);
        _logger.LogInformation("Found {Count} skill file(s) in {Directory}", files.Length, directory);

        foreach (var file in files)
        {
            try
            {
                var skill = await SkillParser.ParseFileAsync(file, ct);
                _skills[skill.Name] = skill;
                CacheTriggers(skill);
                _logger.LogDebug("Loaded skill: {Name} ({File})", skill.Name, file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse skill file: {File}", file);
            }
        }
    }

    /// <summary>Look up a skill by name (case-insensitive).</summary>
    public SkillDefinition? GetSkill(string name) =>
        _skills.TryGetValue(name, out var skill) ? skill : null;

    /// <summary>Return all loaded skills.</summary>
    public IReadOnlyList<SkillDefinition> GetAll() => _skills.Values.ToList();

    /// <summary>
    /// Match user input against all loaded skill triggers.
    /// Returns the first matching skill, or null if no triggers match.
    /// </summary>
    public SkillDefinition? MatchTrigger(string input)
    {
        foreach (var skill in _skills.Values)
        {
            foreach (var trigger in skill.Triggers)
            {
                var regex = _triggerCache.GetOrAdd(trigger.Pattern,
                    p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled));

                if (regex.IsMatch(input))
                    return skill;
            }
        }

        return null;
    }

    private void CacheTriggers(SkillDefinition skill)
    {
        foreach (var trigger in skill.Triggers)
        {
            _triggerCache.GetOrAdd(trigger.Pattern,
                p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled));
        }
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return Path.GetFullPath(path);
    }
}
