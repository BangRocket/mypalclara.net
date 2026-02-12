using System.Text;
using Clara.Core.Configuration;
using Discord;
using Microsoft.Extensions.Logging;

namespace Clara.Cli.Discord;

/// <summary>
/// Extracts content from Discord message attachments.
/// Images → description placeholder, text files → inline content, documents → description.
/// </summary>
public sealed class AttachmentHandler
{
    private readonly ClaraConfig _config;
    private readonly HttpClient _http;
    private readonly ILogger<AttachmentHandler> _logger;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".py", ".js", ".ts", ".jsx", ".tsx", ".json", ".yaml", ".yml",
        ".xml", ".html", ".css", ".cs", ".java", ".go", ".rs", ".rb", ".php",
        ".sh", ".bash", ".zsh", ".fish", ".ps1", ".bat", ".cmd",
        ".toml", ".ini", ".cfg", ".conf", ".env", ".csv", ".sql", ".r", ".lua",
        ".swift", ".kt", ".scala", ".zig", ".nim", ".ex", ".exs", ".erl",
        ".hs", ".ml", ".fs", ".fsx", ".clj", ".lisp", ".el",
        ".dockerfile", ".makefile", ".cmake",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
    };

    public AttachmentHandler(ClaraConfig config, ILogger<AttachmentHandler> logger)
    {
        _config = config;
        _http = new HttpClient();
        _logger = logger;
    }

    /// <summary>Extract content descriptions/text from all attachments on a message.</summary>
    public async Task<string?> ExtractAsync(IReadOnlyCollection<Attachment> attachments)
    {
        if (attachments.Count == 0)
            return null;

        var sb = new StringBuilder();

        foreach (var attachment in attachments)
        {
            var ext = Path.GetExtension(attachment.Filename);

            if (ImageExtensions.Contains(ext))
            {
                var sizeStr = attachment.Size > 1_048_576
                    ? $"{attachment.Size / 1_048_576.0:F1}MB"
                    : $"{attachment.Size / 1024.0:F0}KB";

                var dims = attachment.Width.HasValue && attachment.Height.HasValue
                    ? $"{attachment.Width}x{attachment.Height}, "
                    : "";

                sb.AppendLine($"[Image: {attachment.Filename}, {dims}{sizeStr}]");
            }
            else if (TextExtensions.Contains(ext) || attachment.Filename.EndsWith("file", StringComparison.OrdinalIgnoreCase))
            {
                if (attachment.Size > _config.Discord.MaxTextFileSize)
                {
                    sb.AppendLine($"[Text file: {attachment.Filename}, {attachment.Size / 1024.0:F0}KB — too large to inline]");
                    continue;
                }

                try
                {
                    var content = await _http.GetStringAsync(attachment.Url);
                    sb.AppendLine($"--- {attachment.Filename} ---");
                    sb.AppendLine(content);
                    sb.AppendLine("---");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download attachment {Filename}", attachment.Filename);
                    sb.AppendLine($"[Text file: {attachment.Filename} — download failed]");
                }
            }
            else if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var sizeStr = attachment.Size > 1_048_576
                    ? $"{attachment.Size / 1_048_576.0:F1}MB"
                    : $"{attachment.Size / 1024.0:F0}KB";
                sb.AppendLine($"[Document: {attachment.Filename}, {sizeStr}]");
            }
            else
            {
                sb.AppendLine($"[Attachment: {attachment.Filename}, {attachment.Size / 1024.0:F0}KB]");
            }
        }

        var result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }
}
