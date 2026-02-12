using System.Net.Http.Headers;
using System.Text.Json;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Voice;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Voice.Transcription;

/// <summary>
/// STT via OpenAI-compatible Whisper API (works with OpenAI and Groq).
/// Sends multipart/form-data POST to /v1/audio/transcriptions.
/// </summary>
public sealed class WhisperTranscriber : ITranscriber
{
    private readonly HttpClient _http;
    private readonly SttSettings _settings;
    private readonly ILogger<WhisperTranscriber> _logger;

    public WhisperTranscriber(HttpClient http, ClaraConfig config, ILogger<WhisperTranscriber> logger)
    {
        _http = http;
        _settings = config.Voice.Stt;
        _logger = logger;

        var baseUrl = !string.IsNullOrEmpty(_settings.BaseUrl)
            ? _settings.BaseUrl.TrimEnd('/')
            : _settings.Provider.Equals("groq", StringComparison.OrdinalIgnoreCase)
                ? "https://api.groq.com/openai/v1"
                : "https://api.openai.com/v1";

        _http.BaseAddress = new Uri(baseUrl + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
    }

    public async Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();

        var audioContent = new ByteArrayContent(wavBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "audio.wav");
        content.Add(new StringContent(_settings.Model), "model");
        content.Add(new StringContent("json"), "response_format");

        _logger.LogDebug("Transcribing {Bytes} bytes via {Provider}/{Model}", wavBytes.Length, _settings.Provider, _settings.Model);

        var response = await _http.PostAsync("audio/transcriptions", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var text = doc.RootElement.GetProperty("text").GetString()?.Trim();
        _logger.LogDebug("Transcription result: {Text}", text);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
