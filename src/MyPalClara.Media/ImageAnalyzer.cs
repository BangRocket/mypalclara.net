using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MyPalClara.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Media;

/// <summary>
/// Analyzes images using LLM vision APIs. Supports both Anthropic and OpenAI vision models.
/// </summary>
public sealed class ImageAnalyzer
{
    private readonly HttpClient _http;
    private readonly ClaraConfig _config;
    private readonly ILogger<ImageAnalyzer> _logger;

    public ImageAnalyzer(HttpClient http, ClaraConfig config, ILogger<ImageAnalyzer> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>Analyze an image from a URL.</summary>
    public Task<string> AnalyzeUrlAsync(string imageUrl, string prompt = "Describe this image in detail.", CancellationToken ct = default)
        => AnalyzeAsync(new ImageSource.Url(imageUrl), prompt, ct);

    /// <summary>Analyze an image from raw bytes.</summary>
    public Task<string> AnalyzeBase64Async(byte[] imageData, string mediaType, string prompt = "Describe this image in detail.", CancellationToken ct = default)
        => AnalyzeAsync(new ImageSource.Base64(Convert.ToBase64String(imageData), mediaType), prompt, ct);

    /// <summary>Analyze an image from a file path.</summary>
    public async Task<string> AnalyzeFileAsync(string filePath, string prompt = "Describe this image in detail.", CancellationToken ct = default)
    {
        var data = await File.ReadAllBytesAsync(filePath, ct);
        var mediaType = GetMediaType(filePath);
        return await AnalyzeBase64Async(data, mediaType, prompt, ct);
    }

    private async Task<string> AnalyzeAsync(ImageSource source, string prompt, CancellationToken ct)
    {
        var provider = _config.Llm.ActiveProvider;

        if (_config.Llm.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
            return await AnalyzeWithAnthropicAsync(source, prompt, provider, ct);

        return await AnalyzeWithOpenAiAsync(source, prompt, provider, ct);
    }

    private async Task<string> AnalyzeWithAnthropicAsync(ImageSource source, string prompt, ProviderSettings provider, CancellationToken ct)
    {
        var baseUrl = provider.BaseUrl.TrimEnd('/');
        var url = baseUrl.EndsWith("/v1") ? $"{baseUrl}/messages" : $"{baseUrl}/v1/messages";

        // Build content array with image + text
        var contentParts = new List<object>();

        switch (source)
        {
            case ImageSource.Url urlSource:
                contentParts.Add(new
                {
                    type = "image",
                    source = new { type = "url", url = urlSource.ImageUrl }
                });
                break;
            case ImageSource.Base64 b64Source:
                contentParts.Add(new
                {
                    type = "image",
                    source = new { type = "base64", media_type = b64Source.MediaType, data = b64Source.Data }
                });
                break;
        }

        contentParts.Add(new { type = "text", text = prompt });

        var body = new
        {
            model = provider.Model,
            max_tokens = 4096,
            messages = new[]
            {
                new { role = "user", content = contentParts }
            }
        };

        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Add("x-api-key", provider.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Vision API error: {Status} {Body}", response.StatusCode, responseBody[..Math.Min(200, responseBody.Length)]);
            throw new HttpRequestException($"Vision API returned {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var sb = new StringBuilder();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "text")
                sb.Append(block.GetProperty("text").GetString());
        }
        return sb.ToString();
    }

    private async Task<string> AnalyzeWithOpenAiAsync(ImageSource source, string prompt, ProviderSettings provider, CancellationToken ct)
    {
        var baseUrl = provider.BaseUrl.TrimEnd('/');
        var url = baseUrl.EndsWith("/v1") ? $"{baseUrl}/chat/completions" : $"{baseUrl}/v1/chat/completions";

        var contentParts = new List<object> { new { type = "text", text = prompt } };

        switch (source)
        {
            case ImageSource.Url urlSource:
                contentParts.Add(new { type = "image_url", image_url = new { url = urlSource.ImageUrl } });
                break;
            case ImageSource.Base64 b64Source:
                contentParts.Add(new { type = "image_url", image_url = new { url = $"data:{b64Source.MediaType};base64,{b64Source.Data}" } });
                break;
        }

        var body = new
        {
            model = provider.Model,
            max_tokens = 4096,
            messages = new[]
            {
                new { role = "user", content = contentParts }
            }
        };

        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);

        var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Vision API error: {Status} {Body}", response.StatusCode, responseBody[..Math.Min(200, responseBody.Length)]);
            throw new HttpRequestException($"Vision API returned {response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    private static string GetMediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };

    private abstract record ImageSource
    {
        public sealed record Url(string ImageUrl) : ImageSource;
        public sealed record Base64(string Data, string MediaType) : ImageSource;
    }
}
