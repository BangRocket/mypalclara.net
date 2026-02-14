using System.Runtime.CompilerServices;
using MyPalClara.Core.Chat;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Identity;
using MyPalClara.Core.Llm;
using MyPalClara.Core.Memory;
using MyPalClara.Core.Orchestration;
using MyPalClara.Core.Personality;
using MyPalClara.Agent.Mcp;
using MyPalClara.Agent.Orchestration;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Web.Services;

/// <summary>
/// In-process chat service that bypasses WebSocket. Mirrors MessageRouter.HandleChatAsync()
/// but yields OrchestratorEvents directly for Blazor consumption.
/// </summary>
public sealed class WebChatService
{
    private readonly ClaraConfig _config;
    private readonly UserIdentityService _identity;
    private readonly ChatHistoryService _chatHistory;
    private readonly PersonalityLoader _personality;
    private readonly LlmOrchestrator _orchestrator;
    private readonly McpServerManager _mcp;
    private readonly IMemoryService? _memory;
    private readonly ILogger<WebChatService> _logger;

    // Per-service-scope conversation state
    private Guid? _conversationId;
    private Guid? _channelId;
    private Guid? _userGuid;

    public WebChatService(
        ClaraConfig config,
        UserIdentityService identity,
        ChatHistoryService chatHistory,
        PersonalityLoader personality,
        LlmOrchestrator orchestrator,
        McpServerManager mcp,
        IMemoryService? memory,
        ILogger<WebChatService> logger)
    {
        _config = config;
        _identity = identity;
        _chatHistory = chatHistory;
        _personality = personality;
        _orchestrator = orchestrator;
        _mcp = mcp;
        _memory = memory;
        _logger = logger;
    }

    /// <summary>
    /// Send a message and stream orchestrator events back.
    /// Handles identity resolution, memory fetch, history loading, and background persistence.
    /// </summary>
    public async IAsyncEnumerable<OrchestratorEvent> ChatAsync(
        string message,
        string? tier,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Resolve user identity
        var prefixedUserId = $"web-{_config.UserId}";
        _userGuid ??= await _identity.ResolveUserGuidAsync(prefixedUserId, _config.UserId);

        // 2. Ensure DB channel + conversation
        if (_userGuid is not null && _conversationId is null)
        {
            var channelResult = await _chatHistory.EnsureChannelAsync(
                "web", "Web UI", $"web-{_config.UserId}", "Web Chat", "dm", ct);

            if (channelResult is not null)
            {
                _channelId = channelResult.Value.ChannelId;
                _conversationId = await _chatHistory.GetOrCreateConversationAsync(
                    channelResult.Value.ChannelId, _userGuid.Value, ct);
            }
        }

        // 3. Fetch memory context (best-effort)
        MemoryContext? memoryContext = null;
        if (_memory is not null)
        {
            try
            {
                var allUserIds = await _identity.ResolveAllUserIdsAsync(prefixedUserId);
                memoryContext = await _memory.FetchContextAsync(message, allUserIds, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Memory fetch failed -- continuing without memory context");
            }
        }

        // 4. Build messages
        var messages = new List<ChatMessage>();

        // Personality
        var personality = _personality.GetPersonality();
        messages.Add(new SystemMessage(personality));

        // Memory sections
        if (_memory is not null && memoryContext is not null)
        {
            var memorySections = _memory.BuildPromptSections(memoryContext);
            foreach (var section in memorySections)
            {
                if (!string.IsNullOrEmpty(section))
                    messages.Add(new SystemMessage(section));
            }
        }

        // History from DB
        if (_conversationId is not null)
        {
            var dbHistory = await _chatHistory.LoadRecentMessagesAsync(
                _conversationId.Value, ct: ct);
            messages.AddRange(dbHistory);
        }

        // User message
        messages.Add(new UserMessage(message));

        // 5. Get MCP tool schemas
        var tools = _mcp.GetAllToolSchemas();

        // 6. Stream orchestrator events
        var fullText = "";

        await foreach (var evt in _orchestrator.GenerateWithToolsAsync(
            messages, tools, tier, ct: ct))
        {
            if (evt is CompleteEvent complete)
                fullText = complete.FullText;

            yield return evt;
        }

        // 7. Background: persist chat + memory
        if (_conversationId is not null && !string.IsNullOrWhiteSpace(fullText))
        {
            var convId = _conversationId.Value;
            var userGuid = _userGuid;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _chatHistory.StoreExchangeAsync(convId, userGuid, message, fullText, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background chat history persist failed");
                }
            }, CancellationToken.None);
        }

        if (_memory is not null && !string.IsNullOrWhiteSpace(fullText))
        {
            var userId = prefixedUserId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _memory.AddAsync(message, fullText, userId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background memory ingestion failed");
                }
            }, CancellationToken.None);
        }
    }
}
