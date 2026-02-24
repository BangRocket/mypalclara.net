using System.Net.WebSockets;
using System.Text;
using MyPalClara.Data;
using MyPalClara.Llm;
using MyPalClara.Memory;
using MyPalClara.Memory.FactExtraction;
using MyPalClara.Memory.VectorStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Core.Processing;

/// <summary>
/// Core message processing orchestrator.
/// Registered as a singleton; uses IServiceScopeFactory for scoped DbContext access.
/// </summary>
public class MessageProcessor : IMessageProcessor
{
    /// <summary>
    /// Delegate for sending messages back through WebSocket, avoiding circular dependency on GatewayServer.
    /// </summary>
    public delegate Task SendMessageDelegate(WebSocket ws, object message, CancellationToken ct);

    private readonly ILlmProvider _llm;
    private readonly IRookMemory _rook;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SendMessageDelegate _send;
    private readonly SessionManager _sessionManager;
    private readonly IFactExtractor _factExtractor;
    private readonly SmartIngestion _smartIngestion;
    private readonly ILogger<MessageProcessor> _logger;

    public MessageProcessor(
        ILlmProvider llm,
        IRookMemory rook,
        IServiceScopeFactory scopeFactory,
        SendMessageDelegate send,
        SessionManager sessionManager,
        IFactExtractor factExtractor,
        SmartIngestion smartIngestion,
        ILogger<MessageProcessor> logger)
    {
        _llm = llm;
        _rook = rook;
        _scopeFactory = scopeFactory;
        _send = send;
        _sessionManager = sessionManager;
        _factExtractor = factExtractor;
        _smartIngestion = smartIngestion;
        _logger = logger;
    }

    public async Task ProcessAsync(ProcessingContext context, CancellationToken ct = default)
    {
        var requestId = context.RequestId;
        var responseId = context.ResponseId;
        var ws = context.WebSocket;

        try
        {
            // 1. Determine model tier
            context.ModelTier = context.TierOverride
                                ?? Environment.GetEnvironmentVariable("MODEL_TIER")
                                ?? "mid";

            _logger.LogInformation(
                "Processing request {RequestId} for user {UserId} on {Platform}/{ChannelId} (tier={Tier})",
                requestId, context.UserId, context.Platform, context.ChannelId, context.ModelTier);

            // 2. Send ResponseStart
            await _send(ws, new
            {
                type = "response_start",
                response_id = responseId,
                request_id = requestId,
                model_tier = context.ModelTier
            }, ct);

            // 3. Get/create DB session
            string sessionId;
            string projectId;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();
                (sessionId, projectId) = await _sessionManager.GetOrCreateSessionAsync(
                    db, context.UserId, context.ChannelId, context.IsDm);
                context.DbSessionId = sessionId;

                // Store the user message
                await _sessionManager.StoreMessageAsync(
                    db, sessionId, context.UserId, "user", context.Content);
            }

            // 4. Fetch recent messages
            List<DbMessageDto> recentMessages;
            string? sessionSummary;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();
                recentMessages = await _sessionManager.GetRecentMessagesAsync(db, sessionId);

                // Also get session summary if this is a continuation
                var session = await db.Sessions.FindAsync(sessionId);
                sessionSummary = session?.SessionSummary;

                // If this session has a previous session, get that session's summary
                if (sessionSummary is null && session?.PreviousSessionId is not null)
                {
                    var prevSession = await db.Sessions.FindAsync(session.PreviousSessionId);
                    sessionSummary = prevSession?.SessionSummary;
                }
            }

            // 5. Fetch memory context from Rook
            var userMemories = new List<MemoryItem>();
            var keyMemories = new List<MemoryItem>();

            try
            {
                // Search relevant memories
                var searchResults = await _rook.SearchAsync(
                    context.Content, userId: context.UserId, limit: 10, ct: ct);

                foreach (var result in searchResults)
                {
                    userMemories.Add(new MemoryItem(
                        result.Point.Id,
                        result.Point.Data,
                        result.Score));
                }

                // Get key memories
                var allMemories = await _rook.GetAllAsync(
                    userId: context.UserId, limit: 100, ct: ct);

                foreach (var point in allMemories.Where(p => p.IsKey))
                {
                    keyMemories.Add(new MemoryItem(point.Id, point.Data, null));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to fetch memories for user {UserId}, continuing without memory context",
                    context.UserId);
            }

            // 6. Build LLM messages
            var llmMessages = PromptBuilder.BuildMessages(
                context.Content,
                context.UserId,
                context.DisplayName,
                context.ChannelType,
                context.Platform,
                recentMessages,
                userMemories,
                keyMemories,
                sessionSummary,
                guildName: null);

            // 7. Stream LLM response
            var accumulated = new StringBuilder();
            await foreach (var chunk in _llm.StreamAsync(llmMessages, ct))
            {
                accumulated.Append(chunk);
                await _send(ws, new
                {
                    type = "response_chunk",
                    response_id = responseId,
                    request_id = requestId,
                    chunk,
                    full_text = accumulated.ToString()
                }, ct);
            }

            var fullText = accumulated.ToString();

            // 8. Store assistant message in DB
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClaraDbContext>();
                await _sessionManager.StoreMessageAsync(
                    db, sessionId, "clara", "assistant", fullText);

                // Update session activity
                await _sessionManager.UpdateSessionActivityAsync(db, sessionId);
            }

            // 9. Send ResponseEnd
            await _send(ws, new
            {
                type = "response_end",
                response_id = responseId,
                request_id = requestId,
                full_text = fullText,
                tool_count = 0
            }, ct);

            _logger.LogInformation(
                "Completed request {RequestId}: {Length} chars",
                requestId, fullText.Length);

            // 10. Background: extract memories (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    var facts = await _factExtractor.ExtractAsync(
                        context.Content, fullText, context.UserId, CancellationToken.None);

                    foreach (var fact in facts)
                    {
                        var result = await _smartIngestion.IngestFactAsync(
                            fact, context.UserId, CancellationToken.None);
                        _logger.LogDebug("Ingested fact: {Action} {NewId}", result.Action, result.NewId);
                    }

                    _logger.LogInformation(
                        "Extracted {Count} facts for request {RequestId}", facts.Count, requestId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Background memory extraction failed for request {RequestId}", requestId);
                }
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Request {RequestId} was cancelled", requestId);

            try
            {
                await _send(ws, new
                {
                    type = "cancelled",
                    request_id = requestId,
                    response_id = responseId
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to send cancellation notice for {RequestId}", requestId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process request {RequestId}", requestId);

            try
            {
                await _send(ws, new
                {
                    type = "error",
                    request_id = requestId,
                    response_id = responseId,
                    error = ex.Message
                }, CancellationToken.None);
            }
            catch (Exception sendEx)
            {
                _logger.LogDebug(sendEx, "Failed to send error for {RequestId}", requestId);
            }
        }
    }
}
