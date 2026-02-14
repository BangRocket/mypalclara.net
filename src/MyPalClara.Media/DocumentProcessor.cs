using UglyToad.PdfPig;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Media;

/// <summary>Extracts text content from documents (PDF, plain text, etc.).</summary>
public sealed class DocumentProcessor
{
    private readonly ILogger<DocumentProcessor> _logger;

    public DocumentProcessor(ILogger<DocumentProcessor> logger)
    {
        _logger = logger;
    }

    /// <summary>Extract text from a document file. Supports PDF, TXT, MD, CSV, JSON, XML.</summary>
    public async Task<DocumentResult> ProcessFileAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Document not found: {filePath}");

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        return ext switch
        {
            ".pdf" => ProcessPdf(filePath),
            ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".yaml" or ".yml" or ".log"
                => await ProcessTextFileAsync(filePath, ct),
            _ => throw new NotSupportedException($"Unsupported document format: {ext}"),
        };
    }

    /// <summary>Extract text from raw bytes with a known content type.</summary>
    public DocumentResult ProcessBytes(byte[] data, string contentType, string? fileName = null)
    {
        if (contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return ProcessPdfBytes(data, fileName);

        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            return new DocumentResult(
                Encoding.UTF8.GetString(data),
                PageCount: 1,
                Format: contentType,
                FileName: fileName);
        }

        throw new NotSupportedException($"Unsupported content type: {contentType}");
    }

    private DocumentResult ProcessPdf(string filePath)
    {
        _logger.LogInformation("Processing PDF: {Path}", filePath);

        using var document = PdfDocument.Open(filePath);
        var sb = new StringBuilder();
        var pageCount = 0;

        foreach (var page in document.GetPages())
        {
            pageCount++;
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (sb.Length > 0) sb.AppendLine("\n--- Page Break ---\n");
                sb.Append(text);
            }
        }

        return new DocumentResult(
            sb.ToString(),
            PageCount: pageCount,
            Format: "application/pdf",
            FileName: Path.GetFileName(filePath));
    }

    private DocumentResult ProcessPdfBytes(byte[] data, string? fileName)
    {
        using var stream = new MemoryStream(data);
        using var document = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        var pageCount = 0;

        foreach (var page in document.GetPages())
        {
            pageCount++;
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (sb.Length > 0) sb.AppendLine("\n--- Page Break ---\n");
                sb.Append(text);
            }
        }

        return new DocumentResult(sb.ToString(), PageCount: pageCount, Format: "application/pdf", FileName: fileName);
    }

    private static async Task<DocumentResult> ProcessTextFileAsync(string filePath, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(filePath, ct);
        return new DocumentResult(
            text,
            PageCount: 1,
            Format: $"text/{Path.GetExtension(filePath).TrimStart('.')}",
            FileName: Path.GetFileName(filePath));
    }
}

/// <summary>Result of document processing.</summary>
public sealed record DocumentResult(
    string Text,
    int PageCount,
    string Format,
    string? FileName = null);
