using Microsoft.Extensions.Logging;

namespace Clara.Adapters.Discord;

/// <summary>
/// Handles downloading and preparing images from Discord for processing.
/// </summary>
public class DiscordImageHandler
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DiscordImageHandler> _logger;
    private readonly int _maxDimension;

    public DiscordImageHandler(HttpClient httpClient, ILogger<DiscordImageHandler> logger, int maxDimension = 1568)
    {
        _httpClient = httpClient;
        _logger = logger;
        _maxDimension = maxDimension;
    }

    /// <summary>
    /// Download an image from URL and return as base64.
    /// Discord CDN URLs are publicly accessible.
    /// </summary>
    public async Task<ImageData?> DownloadAsBase64Async(string url, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            _logger.LogDebug("Downloaded image: {Url} ({Size} bytes, {ContentType})",
                url, bytes.Length, contentType);

            var base64 = Convert.ToBase64String(bytes);
            return new ImageData(base64, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download image: {Url}", url);
            return null;
        }
    }
}

public record ImageData(string Base64, string MediaType);
