namespace MyPalClara.Tools.Backfill.Models;

/// <summary>A single conversation from any source, normalized for backfill processing.</summary>
public sealed record BackfillConversation(
    string SourceId,
    string SourceType,
    string Title,
    List<BackfillExchange> Exchanges);

/// <summary>A user-assistant message pair — the unit of processing.</summary>
public sealed record BackfillExchange(
    string UserMessage,
    string AssistantMessage,
    DateTime UserTimestamp,
    DateTime AssistantTimestamp,
    string? UserDisplayName = null);
