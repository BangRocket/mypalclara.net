using MyPalClara.Llm;
using MyPalClara.Modules.Proactive.Engine;

namespace MyPalClara.Modules.Proactive.Prompts;

public static class AssessmentPrompt
{
    public static IReadOnlyList<LlmMessage> Build(OrsContext context)
    {
        var system = """
            You are Clara's proactive outreach assessment system.
            Given context about a user, synthesize a brief situation summary.
            Focus on: emotional state signals, unfinished conversations, upcoming events,
            and opportunities for genuine connection.
            Be concise (2-3 sentences max).
            """;

        var user = $"""
            User: {context.UserId}
            Temporal: {context.TemporalSummary ?? "unknown"}
            Last conversation: {context.ConversationSummary ?? "none"}
            Cross-channel: {context.CrossChannelSummary ?? "none"}
            Cadence: {context.CadenceSummary ?? "unknown"}
            Calendar: {context.CalendarSummary ?? "none"}
            Active notes: {(context.ActiveNotes.Count > 0 ? string.Join("\n", context.ActiveNotes) : "none")}
            Last spoke: {context.LastSpokeAt?.ToString("O") ?? "never"}
            Last user activity: {context.LastUserActivityAt?.ToString("O") ?? "unknown"}
            """;

        return new LlmMessage[] { new SystemMessage(system), new UserMessage(user) };
    }
}
