using System.Text;
using MyPalClara.Llm;

namespace MyPalClara.Core.Processing;

/// <summary>
/// Data transfer objects for prompt building.
/// </summary>
public record DbMessageDto(string Role, string Content, DateTime CreatedAt);

public record MemoryItem(string Id, string Content, float? Score);

/// <summary>
/// Builds the system prompt and message list for LLM invocation.
/// </summary>
public static class PromptBuilder
{
    private const string ClaraPersona = """
        You are Clara, a personal AI assistant created to be genuinely helpful, warm, and thoughtful.

        ## Core Traits
        - Warm but not saccharine — authentic care, not performance
        - Technically capable — you can discuss code, science, philosophy with depth
        - Memory-aware — you remember past conversations and personal details
        - Adaptive — you match the user's energy and communication style

        ## Guidelines
        - Be concise unless depth is requested
        - Use the user's name naturally when you know it
        - Reference past conversations when relevant
        - Be honest about uncertainty
        - Help proactively when you notice opportunities
        """;

    /// <summary>
    /// Build the full message list for LLM invocation.
    /// </summary>
    public static IReadOnlyList<LlmMessage> BuildMessages(
        string userContent,
        string userId,
        string? displayName,
        string channelType,
        string platform,
        IReadOnlyList<DbMessageDto>? recentMessages,
        IReadOnlyList<MemoryItem>? userMemories,
        IReadOnlyList<MemoryItem>? keyMemories,
        string? sessionSummary,
        string? guildName)
    {
        var messages = new List<LlmMessage>();

        // 1. System message with persona + memory + context
        var systemPrompt = BuildSystemPrompt(
            channelType, platform, userMemories, keyMemories, sessionSummary, guildName);
        messages.Add(new SystemMessage(systemPrompt));

        // 2. Recent conversation history
        if (recentMessages is { Count: > 0 })
        {
            foreach (var msg in recentMessages)
            {
                LlmMessage llmMsg = msg.Role.ToLowerInvariant() switch
                {
                    "user" => new UserMessage(msg.Content),
                    "assistant" => new AssistantMessage(Content: msg.Content),
                    _ => new UserMessage(msg.Content) // Fallback: treat unknown roles as user
                };
                messages.Add(llmMsg);
            }
        }

        // 3. Current user message (with display name prefix in group chats)
        var currentContent = channelType != "dm" && displayName is not null
            ? $"[{displayName}]: {userContent}"
            : userContent;
        messages.Add(new UserMessage(currentContent));

        return messages;
    }

    private static string BuildSystemPrompt(
        string channelType,
        string platform,
        IReadOnlyList<MemoryItem>? userMemories,
        IReadOnlyList<MemoryItem>? keyMemories,
        string? sessionSummary,
        string? guildName)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ClaraPersona);

        // Memory section: key memories
        if (keyMemories is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## What I Remember About You");
            foreach (var mem in keyMemories)
                sb.AppendLine($"- {mem.Content}");
        }

        // Memory section: search results
        if (userMemories is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## Relevant Context");
            foreach (var mem in userMemories)
                sb.AppendLine($"- {mem.Content}");
        }

        // Session summary
        if (!string.IsNullOrWhiteSpace(sessionSummary))
        {
            sb.AppendLine();
            sb.AppendLine("## Previous Session Summary");
            sb.AppendLine(sessionSummary);
        }

        // Gateway context
        sb.AppendLine();
        sb.AppendLine("## Current Context");
        sb.AppendLine($"- Current time: {DateTime.UtcNow:O}");
        sb.AppendLine($"- Platform: {platform}");
        sb.AppendLine($"- Channel type: {channelType}");
        if (guildName is not null)
            sb.AppendLine($"- Server: {guildName}");

        return sb.ToString();
    }
}
