using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MyPalClara.Agent.Orchestration;
using MyPalClara.Core.Llm;
using MyPalClara.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Skills;

/// <summary>
/// Resolves template variables, filters tools, and delegates to LlmOrchestrator.
/// </summary>
public sealed class SkillExecutor
{
    private static readonly Regex TemplatePlaceholder =
        new(@"\{\{\s*input\.(\w+)\s*\}\}", RegexOptions.Compiled);

    private readonly LlmOrchestrator _orchestrator;
    private readonly ILogger<SkillExecutor> _logger;

    public SkillExecutor(LlmOrchestrator orchestrator, ILogger<SkillExecutor> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Execute a skill: resolve template placeholders, filter tools, and generate via orchestrator.
    /// </summary>
    public async IAsyncEnumerable<OrchestratorEvent> ExecuteAsync(
        SkillDefinition skill,
        Dictionary<string, string> inputValues,
        string userMessage,
        IReadOnlyList<ToolSchema> availableTools,
        string? tier = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Apply defaults for missing optional inputs
        var resolvedInputs = new Dictionary<string, string>(inputValues, StringComparer.OrdinalIgnoreCase);
        foreach (var input in skill.Inputs)
        {
            if (!resolvedInputs.ContainsKey(input.Name) && input.Default is not null)
                resolvedInputs[input.Name] = input.Default;
        }

        // Validate required inputs
        foreach (var input in skill.Inputs.Where(i => i.Required))
        {
            if (!resolvedInputs.ContainsKey(input.Name) || string.IsNullOrWhiteSpace(resolvedInputs[input.Name]))
                throw new ArgumentException($"Required skill input '{input.Name}' is missing");
        }

        // Resolve template placeholders
        var resolvedPrompt = TemplatePlaceholder.Replace(skill.PromptTemplate, match =>
        {
            var name = match.Groups[1].Value;
            return resolvedInputs.TryGetValue(name, out var value) ? value : match.Value;
        });

        // Filter tools to only those required by the skill (if specified)
        var tools = FilterTools(skill, availableTools);

        _logger.LogInformation("Executing skill '{Name}' with {ToolCount} tools",
            skill.Name, tools.Count);

        // Build messages
        var messages = new List<ChatMessage>
        {
            new SystemMessage(resolvedPrompt),
            new UserMessage(userMessage),
        };

        // Delegate to orchestrator
        await foreach (var evt in _orchestrator.GenerateWithToolsAsync(messages, tools, tier, ct: ct))
        {
            yield return evt;
        }
    }

    private static IReadOnlyList<ToolSchema> FilterTools(
        SkillDefinition skill, IReadOnlyList<ToolSchema> availableTools)
    {
        if (skill.ToolsRequired.Count == 0)
            return availableTools;

        var required = new HashSet<string>(skill.ToolsRequired, StringComparer.OrdinalIgnoreCase);
        return availableTools.Where(t => required.Contains(t.Name)).ToList();
    }
}
