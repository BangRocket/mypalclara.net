using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Clara.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Voice;

/// <summary>
/// TTS via Replicate Qwen3-TTS. Direct HTTP: POST prediction → poll → download output.
/// No official .NET SDK; matches existing project patterns (AnthropicProvider, EmbeddingClient).
/// </summary>
public sealed class ReplicateTtsSynthesizer : ITtsSynthesizer
{
    private readonly HttpClient _http;
    private readonly TtsSettings _settings;
    private readonly ILogger<ReplicateTtsSynthesizer> _logger;

    private const string ModelVersion = "heroicbacon/qwen3-tts";
    private const string PredictionsUrl = "https://api.replicate.com/v1/predictions";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(60);

    public ReplicateTtsSynthesizer(HttpClient http, ClaraConfig config, ILogger<ReplicateTtsSynthesizer> logger)
    {
        _http = http;
        _settings = config.Voice.Tts;
        _logger = logger;

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ReplicateApiToken);
    }

    public async Task<byte[]?> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        // Create prediction
        var payload = new
        {
            model = ModelVersion,
            input = new
            {
                text,
                speaker = _settings.Speaker,
                language = _settings.Language,
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, PredictionsUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        _logger.LogDebug("Creating TTS prediction for {Length} chars, speaker={Speaker}", text.Length, _settings.Speaker);

        var createResponse = await _http.SendAsync(createRequest, ct);
        createResponse.EnsureSuccessStatusCode();

        var createJson = await createResponse.Content.ReadAsStringAsync(ct);
        using var createDoc = JsonDocument.Parse(createJson);
        var predictionUrl = createDoc.RootElement.GetProperty("urls").GetProperty("get").GetString()
            ?? throw new InvalidOperationException("No prediction URL in Replicate response");

        // Poll for completion
        var deadline = DateTime.UtcNow + MaxWait;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(PollInterval, ct);

            using var pollRequest = new HttpRequestMessage(HttpMethod.Get, predictionUrl);
            var pollResponse = await _http.SendAsync(pollRequest, ct);
            pollResponse.EnsureSuccessStatusCode();

            var pollJson = await pollResponse.Content.ReadAsStringAsync(ct);
            using var pollDoc = JsonDocument.Parse(pollJson);
            var status = pollDoc.RootElement.GetProperty("status").GetString();

            switch (status)
            {
                case "succeeded":
                    var output = pollDoc.RootElement.GetProperty("output");
                    var audioUrl = output.ValueKind == JsonValueKind.String
                        ? output.GetString()
                        : output.EnumerateArray().FirstOrDefault().GetString();

                    if (string.IsNullOrEmpty(audioUrl))
                    {
                        _logger.LogWarning("TTS prediction succeeded but no audio URL in output");
                        return null;
                    }

                    _logger.LogDebug("Downloading TTS audio from {Url}", audioUrl);
                    return await _http.GetByteArrayAsync(audioUrl, ct);

                case "failed":
                case "canceled":
                    var error = pollDoc.RootElement.TryGetProperty("error", out var errProp)
                        ? errProp.GetString() : "unknown";
                    _logger.LogWarning("TTS prediction {Status}: {Error}", status, error);
                    return null;
            }
        }

        _logger.LogWarning("TTS prediction timed out after {MaxWait}", MaxWait);
        return null;
    }
}
