namespace Clara.Core.Configuration;

public sealed class DiscordSettings
{
    public bool Enabled { get; set; }
    public string BotToken { get; set; } = "";
    public string AllowedServers { get; set; } = "";
    public string AllowedChannels { get; set; } = "";
    public string StopPhrases { get; set; } = "clara stop,stop clara,nevermind,never mind";
    public int MaxHistoryMessages { get; set; } = 50;
    public int MaxImageDimension { get; set; } = 1568;
    public long MaxImageSize { get; set; } = 4_194_304;
    public long MaxTextFileSize { get; set; } = 102_400;

    public HashSet<ulong> ParsedAllowedServers =>
        string.IsNullOrWhiteSpace(AllowedServers)
            ? []
            : AllowedServers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ulong.Parse)
                .ToHashSet();

    public HashSet<ulong> ParsedAllowedChannels =>
        string.IsNullOrWhiteSpace(AllowedChannels)
            ? []
            : AllowedChannels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ulong.Parse)
                .ToHashSet();

    public HashSet<string> ParsedStopPhrases =>
        StopPhrases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
