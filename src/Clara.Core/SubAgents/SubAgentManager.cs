using System.Collections.Concurrent;
using Clara.Core.Config;
using Clara.Core.Events;
using Clara.Core.Llm;
using Clara.Core.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clara.Core.SubAgents;

public class SubAgentManager : ISubAgentManager
{
    private readonly ConcurrentDictionary<string, SubAgentState> _agents = new();
    private readonly ILlmProviderFactory _providerFactory;
    private readonly ISessionManager _sessionManager;
    private readonly IClaraEventBus _eventBus;
    private readonly SubAgentOptions _options;
    private readonly ILogger<SubAgentManager> _logger;

    public SubAgentManager(
        ILlmProviderFactory providerFactory,
        ISessionManager sessionManager,
        IClaraEventBus eventBus,
        IOptions<SubAgentOptions> options,
        ILogger<SubAgentManager> logger)
    {
        _providerFactory = providerFactory;
        _sessionManager = sessionManager;
        _eventBus = eventBus;
        _options = options.Value;
        _logger = logger;
    }

    public Task<string> SpawnAsync(SubAgentRequest request, CancellationToken ct = default)
    {
        // Check max-per-parent limit
        var activeCount = _agents.Values
            .Count(a => a.ParentSessionKey == request.ParentSessionKey && a.Result is null);

        if (activeCount >= _options.MaxPerParent)
            throw new InvalidOperationException(
                $"Maximum sub-agents ({_options.MaxPerParent}) reached for session {request.ParentSessionKey}");

        // Generate sub-task ID
        var subTaskId = GenerateSubTaskId();

        // Create session key for the sub-agent
        var parentKey = SessionKey.Parse(request.ParentSessionKey);
        var subSessionKey = parentKey with { SubTaskId = subTaskId };

        // Create the sub-agent state
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(request.TimeoutMinutes));

        var state = new SubAgentState
        {
            SubTaskId = subTaskId,
            ParentSessionKey = request.ParentSessionKey,
            Cts = cts,
            StartedAt = DateTime.UtcNow
        };

        _agents[subTaskId] = state;

        // Start background task
        state.RunningTask = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Sub-agent {SubTaskId} started for session {SessionKey}",
                    subTaskId, request.ParentSessionKey);

                // Create session for the sub-agent
                await _sessionManager.GetOrCreateAsync(subSessionKey.ToString(), ct: cts.Token);

                // Get LLM provider and model
                var provider = _providerFactory.GetProvider();
                var model = _providerFactory.ResolveModel(provider.Name, request.Tier);

                // Send task as user message
                var llmRequest = new LlmRequest(
                    model,
                    [
                        LlmMessage.System("You are a sub-agent performing a specific task. Be concise and focused."),
                        LlmMessage.User(request.Task)
                    ],
                    Temperature: 0.3f);

                var response = await provider.CompleteAsync(llmRequest, cts.Token);
                var text = string.Join("", response.Content.OfType<TextContent>().Select(c => c.Text));

                state.Result = new SubAgentResult(subTaskId, true, text);

                _logger.LogInformation("Sub-agent {SubTaskId} completed successfully", subTaskId);

                await _eventBus.PublishAsync(new ClaraEvent(SubAgentEvents.Completed, DateTime.UtcNow,
                    new Dictionary<string, object>
                    {
                        ["subTaskId"] = subTaskId,
                        ["parentSessionKey"] = request.ParentSessionKey,
                        ["success"] = true
                    })
                {
                    SessionKey = request.ParentSessionKey
                });
            }
            catch (OperationCanceledException)
            {
                state.Result = new SubAgentResult(subTaskId, false, "", "Cancelled or timed out");

                _logger.LogWarning("Sub-agent {SubTaskId} cancelled or timed out", subTaskId);

                await _eventBus.PublishAsync(new ClaraEvent(SubAgentEvents.Completed, DateTime.UtcNow,
                    new Dictionary<string, object>
                    {
                        ["subTaskId"] = subTaskId,
                        ["parentSessionKey"] = request.ParentSessionKey,
                        ["success"] = false,
                        ["error"] = "Cancelled or timed out"
                    })
                {
                    SessionKey = request.ParentSessionKey
                });
            }
            catch (Exception ex)
            {
                state.Result = new SubAgentResult(subTaskId, false, "", ex.Message);

                _logger.LogError(ex, "Sub-agent {SubTaskId} failed", subTaskId);

                await _eventBus.PublishAsync(new ClaraEvent(SubAgentEvents.Completed, DateTime.UtcNow,
                    new Dictionary<string, object>
                    {
                        ["subTaskId"] = subTaskId,
                        ["parentSessionKey"] = request.ParentSessionKey,
                        ["success"] = false,
                        ["error"] = ex.Message
                    })
                {
                    SessionKey = request.ParentSessionKey
                });
            }
        }, CancellationToken.None); // Don't pass ct here - the task manages its own cancellation via the linked CTS

        return Task.FromResult(subTaskId);
    }

    public Task<SubAgentResult?> GetResultAsync(string subTaskId, CancellationToken ct = default)
    {
        if (_agents.TryGetValue(subTaskId, out var state))
            return Task.FromResult(state.Result);

        return Task.FromResult<SubAgentResult?>(null);
    }

    public IReadOnlyList<string> GetActiveSubAgents(string parentSessionKey)
    {
        return _agents.Values
            .Where(a => a.ParentSessionKey == parentSessionKey && a.Result is null)
            .Select(a => a.SubTaskId)
            .ToList();
    }

    public Task CancelAsync(string subTaskId, CancellationToken ct = default)
    {
        if (_agents.TryGetValue(subTaskId, out var state))
        {
            state.Cts.Cancel();
            _logger.LogInformation("Sub-agent {SubTaskId} cancel requested", subTaskId);
        }

        return Task.CompletedTask;
    }

    private static string GenerateSubTaskId()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }
}

internal class SubAgentState
{
    public string SubTaskId { get; set; } = "";
    public string ParentSessionKey { get; set; } = "";
    public SubAgentResult? Result { get; set; }
    public CancellationTokenSource Cts { get; set; } = new();
    public Task? RunningTask { get; set; }
    public DateTime StartedAt { get; set; }
}
