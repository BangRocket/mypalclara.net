using MyPalClara.Core.Configuration;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using WebRtcVadSharp;

namespace MyPalClara.Voice.Audio;

/// <summary>
/// Captures audio from the microphone via NAudio WaveInEvent,
/// runs WebRTC VAD frame-by-frame, and delivers complete utterances
/// (as WAV byte arrays) via an async callback.
/// </summary>
public sealed class MicrophoneListener : IDisposable
{
    private readonly VadSettings _vadSettings;
    private readonly ILogger _logger;
    private readonly Func<byte[], Task> _onUtterance;

    private WaveInEvent? _waveIn;
    private WebRtcVad? _vad;

    // 16kHz mono 16-bit — required by WebRTC VAD
    private static readonly WaveFormat CaptureFormat = new(16000, 16, 1);

    // VAD processes 30ms frames (480 samples × 2 bytes = 960 bytes at 16kHz)
    private const int FrameDurationMs = 30;
    private const int FrameBytes = 16000 * 2 * FrameDurationMs / 1000; // 960

    // Accumulate voiced frames
    private readonly MemoryStream _voiceBuffer = new();
    private DateTime _lastVoiceTime = DateTime.MinValue;
    private bool _inUtterance;

    public bool IsListening { get; private set; }

    public MicrophoneListener(VadSettings vadSettings, ILogger logger, Func<byte[], Task> onUtterance)
    {
        _vadSettings = vadSettings;
        _logger = logger;
        _onUtterance = onUtterance;
    }

    public void Start()
    {
        if (IsListening) return;

        _vad = new WebRtcVad
        {
            OperatingMode = (OperatingMode)Math.Clamp(_vadSettings.Aggressiveness, 0, 3),
            FrameLength = FrameLength.Is30ms,
            SampleRate = SampleRate.Is16kHz,
        };

        _waveIn = new WaveInEvent
        {
            WaveFormat = CaptureFormat,
            BufferMilliseconds = FrameDurationMs,
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
        IsListening = true;
        _logger.LogDebug("Microphone listener started (VAD aggressiveness={Agg})", _vadSettings.Aggressiveness);
    }

    public void Stop()
    {
        if (!IsListening) return;

        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        _vad?.Dispose();
        _vad = null;

        // Deliver any remaining utterance
        if (_inUtterance && _voiceBuffer.Length > 0)
            DeliverUtterance();

        _voiceBuffer.SetLength(0);
        _inUtterance = false;
        IsListening = false;
        _logger.LogDebug("Microphone listener stopped");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_vad is null) return;

        // Process complete frames from the buffer
        var offset = 0;
        while (offset + FrameBytes <= e.BytesRecorded)
        {
            var frame = new byte[FrameBytes];
            Buffer.BlockCopy(e.Buffer, offset, frame, 0, FrameBytes);
            var isSpeech = _vad.HasSpeech(frame);
            offset += FrameBytes;

            if (isSpeech)
            {
                _voiceBuffer.Write(e.Buffer, offset - FrameBytes, FrameBytes);
                _lastVoiceTime = DateTime.UtcNow;
                _inUtterance = true;
            }
            else if (_inUtterance)
            {
                var silenceElapsed = (DateTime.UtcNow - _lastVoiceTime).TotalSeconds;
                if (silenceElapsed >= _vadSettings.SilenceDuration)
                {
                    DeliverUtterance();
                }
            }
        }
    }

    private void DeliverUtterance()
    {
        if (_voiceBuffer.Length == 0) return;

        var pcmData = _voiceBuffer.ToArray();
        _voiceBuffer.SetLength(0);
        _inUtterance = false;

        // Wrap PCM in a WAV container
        var wavBytes = WrapInWav(pcmData, CaptureFormat);
        _logger.LogDebug("Utterance detected: {Bytes} PCM bytes, {WavBytes} WAV bytes",
            pcmData.Length, wavBytes.Length);

        // Fire-and-forget the async callback (runs on thread pool)
        _ = Task.Run(() => _onUtterance(wavBytes));
    }

    private static byte[] WrapInWav(byte[] pcmData, WaveFormat format)
    {
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, format))
        {
            writer.Write(pcmData, 0, pcmData.Length);
        }
        return ms.ToArray();
    }

    public void Dispose()
    {
        Stop();
        _voiceBuffer.Dispose();
    }
}
