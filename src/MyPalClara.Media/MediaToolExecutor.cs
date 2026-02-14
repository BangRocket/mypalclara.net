using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Media;

/// <summary>Executes media tool calls from the agent.</summary>
public sealed class MediaToolExecutor
{
    private readonly ImageAnalyzer _imageAnalyzer;
    private readonly DocumentProcessor _documentProcessor;
    private readonly HttpClient _http;
    private readonly ILogger<MediaToolExecutor> _logger;

    private static readonly HashSet<string> ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg"];

    public MediaToolExecutor(
        ImageAnalyzer imageAnalyzer,
        DocumentProcessor documentProcessor,
        HttpClient http,
        ILogger<MediaToolExecutor> logger)
    {
        _imageAnalyzer = imageAnalyzer;
        _documentProcessor = documentProcessor;
        _http = http;
        _logger = logger;
    }

    /// <summary>Returns true if this executor handles the given tool name.</summary>
    public static bool CanHandle(string toolName) =>
        toolName.StartsWith("media__", StringComparison.OrdinalIgnoreCase);

    /// <summary>Execute a media tool call and return the result text.</summary>
    public async Task<string> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        return toolName switch
        {
            "media__analyze_image" => await HandleAnalyzeImageAsync(arguments, ct),
            "media__extract_document" => await HandleExtractDocumentAsync(arguments, ct),
            "media__download_and_analyze" => await HandleDownloadAndAnalyzeAsync(arguments, ct),
            _ => throw new ArgumentException($"Unknown media tool: {toolName}"),
        };
    }

    private async Task<string> HandleAnalyzeImageAsync(JsonElement args, CancellationToken ct)
    {
        var source = args.GetProperty("source").GetString()
            ?? throw new ArgumentException("'source' is required");
        var prompt = args.TryGetProperty("prompt", out var p) ? p.GetString() ?? "Describe this image in detail." : "Describe this image in detail.";

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            return await _imageAnalyzer.AnalyzeUrlAsync(source, prompt, ct);
        }

        return await _imageAnalyzer.AnalyzeFileAsync(source, prompt, ct);
    }

    private async Task<string> HandleExtractDocumentAsync(JsonElement args, CancellationToken ct)
    {
        var filePath = args.GetProperty("file_path").GetString()
            ?? throw new ArgumentException("'file_path' is required");

        var result = await _documentProcessor.ProcessFileAsync(filePath, ct);
        return $"[Document: {result.FileName}, {result.PageCount} page(s), format: {result.Format}]\n\n{result.Text}";
    }

    private async Task<string> HandleDownloadAndAnalyzeAsync(JsonElement args, CancellationToken ct)
    {
        var url = args.GetProperty("url").GetString()
            ?? throw new ArgumentException("'url' is required");
        var prompt = args.TryGetProperty("prompt", out var p) ? p.GetString() ?? "Describe this image in detail." : "Describe this image in detail.";

        _logger.LogInformation("Downloading: {Url}", url);

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var data = await response.Content.ReadAsByteArrayAsync(ct);
        var fileName = Path.GetFileName(new Uri(url).AbsolutePath);

        // Determine if image or document
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            ImageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant()))
        {
            var mediaType = contentType.StartsWith("image/") ? contentType : "image/jpeg";
            return await _imageAnalyzer.AnalyzeBase64Async(data, mediaType, prompt, ct);
        }

        var result = _documentProcessor.ProcessBytes(data, contentType, fileName);
        return $"[Document: {result.FileName}, {result.PageCount} page(s), format: {result.Format}]\n\n{result.Text}";
    }
}
