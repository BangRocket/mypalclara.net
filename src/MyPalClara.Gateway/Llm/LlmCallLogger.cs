using MyPalClara.Core.Data;
using MyPalClara.Core.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Gateway.Llm;

/// <summary>
/// Logs every LLM API call (request/response, tokens, latency) to the llm_calls table.
/// </summary>
public sealed class LlmCallLogger
{
    private readonly IDbContextFactory<ClaraDbContext> _dbFactory;
    private readonly ILogger<LlmCallLogger> _logger;

    public LlmCallLogger(IDbContextFactory<ClaraDbContext> dbFactory, ILogger<LlmCallLogger> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task LogCallAsync(
        Guid? conversationId,
        string model,
        string provider,
        string requestBody,
        string responseBody,
        int? inputTokens = null,
        int? outputTokens = null,
        int? cacheReadTokens = null,
        int? cacheWriteTokens = null,
        int? latencyMs = null,
        string status = "success",
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            db.LlmCalls.Add(new LlmCallEntity
            {
                ConversationId = conversationId,
                Model = model,
                Provider = provider,
                RequestBody = requestBody,
                ResponseBody = responseBody,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CacheReadTokens = cacheReadTokens,
                CacheWriteTokens = cacheWriteTokens,
                LatencyMs = latencyMs,
                Status = status,
                ErrorMessage = errorMessage,
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to log LLM call");
        }
    }
}
