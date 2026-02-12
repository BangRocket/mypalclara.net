using Clara.Core.Configuration;
using Clara.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Clara.Cli.Voice;

/// <summary>
/// Orchestrates the full voice pipeline: mic capture → VAD → STT → callback,
/// and TTS → speaker playback. Lifecycle managed by !voice on/off commands.
/// </summary>
public sealed class VoiceManager : IDisposable
{
    private readonly ClaraConfig _config;
    private readonly ITranscriber _transcriber;
    private readonly ITtsSynthesizer _tts;
    private readonly ILogger<VoiceManager> _logger;

    private MicrophoneListener? _listener;
    private AudioPlayer? _player;

    /// <summary>Callback invoked when a voice utterance is transcribed to text.</summary>
    public Func<string, Task>? OnTranscription { get; set; }

    public bool IsActive => _listener?.IsListening == true;

    public VoiceManager(
        ClaraConfig config,
        ITranscriber transcriber,
        ITtsSynthesizer tts,
        ILogger<VoiceManager> logger)
    {
        _config = config;
        _transcriber = transcriber;
        _tts = tts;
        _logger = logger;
    }

    public void Start()
    {
        if (IsActive) return;

        _player = new AudioPlayer(_logger);
        _player.Start();

        _listener = new MicrophoneListener(
            _config.Voice.Vad,
            _logger,
            OnUtteranceAsync);
        _listener.Start();

        _logger.LogInformation("Voice mode activated");
    }

    public void Stop()
    {
        _listener?.Stop();
        _listener?.Dispose();
        _listener = null;

        _player?.Stop();
        _player?.Dispose();
        _player = null;

        _logger.LogInformation("Voice mode deactivated");
    }

    private async Task OnUtteranceAsync(byte[] wavBytes)
    {
        try
        {
            var text = await _transcriber.TranscribeAsync(wavBytes);
            if (string.IsNullOrWhiteSpace(text)) return;

            // Interrupt current playback if user starts speaking
            if (_config.Voice.EnableInterruption && _player?.IsPlaying == true)
                _player.Interrupt();

            // Deliver transcription to the REPL
            if (OnTranscription is not null)
                await OnTranscription(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Voice utterance processing failed");
        }
    }

    /// <summary>Synthesize text to speech and play through speakers.</summary>
    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (_player is null || !IsActive) return;

        try
        {
            var audioBytes = await _tts.SynthesizeAsync(text, ct);
            if (audioBytes is not null)
                await _player.EnqueueAsync(audioBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TTS synthesis/playback failed");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
