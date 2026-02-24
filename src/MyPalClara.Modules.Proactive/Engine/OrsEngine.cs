using Microsoft.Extensions.Logging;
using MyPalClara.Llm;
using MyPalClara.Modules.Proactive.Delivery;
using MyPalClara.Modules.Proactive.Notes;
using MyPalClara.Modules.Proactive.Prompts;

namespace MyPalClara.Modules.Proactive.Engine;

public class OrsEngine
{
    private readonly ILlmProvider _llm;
    private readonly NoteManager _notes;
    private readonly OutreachDelivery _delivery;
    private readonly ILogger<OrsEngine> _logger;
    private readonly double _minSpeakGapHours;
    private readonly int _noteDecayDays;

    public OrsEngine(ILlmProvider llm, NoteManager notes, OutreachDelivery delivery,
        ILogger<OrsEngine> logger)
    {
        _llm = llm;
        _notes = notes;
        _delivery = delivery;
        _logger = logger;
        _minSpeakGapHours = double.TryParse(
            Environment.GetEnvironmentVariable("ORS_MIN_SPEAK_GAP_HOURS"), out var h) ? h : 2.0;
        _noteDecayDays = int.TryParse(
            Environment.GetEnvironmentVariable("ORS_NOTE_DECAY_DAYS"), out var d) ? d : 7;
    }

    public async Task<OrsDecision> AssessUserAsync(OrsContext context, CancellationToken ct = default)
    {
        // Boundary check: min gap
        if (context.LastSpokeAt is not null &&
            (DateTime.UtcNow - context.LastSpokeAt.Value).TotalHours < _minSpeakGapHours)
        {
            return new OrsDecision(OrsState.Wait, "Too soon since last outreach");
        }

        // Stage 1: Assessment prompt
        var assessmentMessages = AssessmentPrompt.Build(context);
        var assessmentResponse = await _llm.InvokeAsync(assessmentMessages, ct: ct);
        var assessment = assessmentResponse.Content ?? "";

        // Stage 2: Decision prompt
        var decisionMessages = DecisionPrompt.Build(context, assessment);
        var decisionResponse = await _llm.InvokeAsync(decisionMessages, ct: ct);
        var decisionText = decisionResponse.Content ?? "WAIT";

        // Parse structured decision
        var decision = OrsDecision.Parse(decisionText);

        _logger.LogInformation("ORS assessment for {User}: {State} ({Reasoning})",
            context.UserId, decision.NextState, decision.Reasoning ?? "none");

        return decision;
    }

    public async Task ExecuteDecisionAsync(OrsContext context, OrsDecision decision, CancellationToken ct = default)
    {
        switch (decision.NextState)
        {
            case OrsState.Wait:
                _logger.LogDebug("ORS: WAIT for {User}", context.UserId);
                break;

            case OrsState.Think:
                if (decision.NoteContent is not null)
                {
                    await _notes.CreateNoteAsync(context.UserId, decision.NoteContent, "observation", ct);
                    _logger.LogInformation("ORS: THINK for {User} -- created note", context.UserId);
                }
                break;

            case OrsState.Speak:
                if (decision.MessageContent is not null)
                {
                    await _delivery.SendAsync(context.UserId, decision.MessageContent, ct);
                    _logger.LogInformation("ORS: SPEAK to {User}", context.UserId);
                }
                break;
        }
    }
}
