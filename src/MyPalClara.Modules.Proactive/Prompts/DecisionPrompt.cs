using MyPalClara.Llm;
using MyPalClara.Modules.Proactive.Engine;

namespace MyPalClara.Modules.Proactive.Prompts;

public static class DecisionPrompt
{
    public static IReadOnlyList<LlmMessage> Build(OrsContext context, string assessment)
    {
        var system = """
            You are Clara's proactive outreach decision engine.
            Given a situation assessment, decide ONE action:

            - WAIT: No action. User is busy, recently contacted, or nothing meaningful to say.
            - THINK: Create an internal note (observation, question, follow-up, or connection).
            - SPEAK: Send a proactive message to the user.

            Respond with EXACTLY one word: WAIT, THINK, or SPEAK.
            Err on the side of WAIT. Only SPEAK when there is genuine value to add.
            """;

        var user = $"""
            Assessment: {assessment}
            Current state: {context.CurrentState}
            """;

        return new LlmMessage[] { new SystemMessage(system), new UserMessage(user) };
    }
}
