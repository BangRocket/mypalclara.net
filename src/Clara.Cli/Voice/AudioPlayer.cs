using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Clara.Cli.Voice;

/// <summary>
/// Async audio playback queue. Enqueue audio byte arrays (WAV format),
/// they play sequentially through the system speaker via NAudio WaveOutEvent.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly ILogger _logger;
    private readonly Channel<byte[]> _queue = Channel.CreateBounded<byte[]>(16);
    private CancellationTokenSource? _cts;
    private Task? _playbackTask;

    public bool IsPlaying { get; private set; }

    public AudioPlayer(ILogger logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        if (_playbackTask is not null) return;

        _cts = new CancellationTokenSource();
        _playbackTask = Task.Run(() => PlaybackLoop(_cts.Token));
        _logger.LogDebug("Audio player started");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _playbackTask = null;
        _cts?.Dispose();
        _cts = null;
        IsPlaying = false;
        _logger.LogDebug("Audio player stopped");
    }

    public async ValueTask EnqueueAsync(byte[] audioBytes)
    {
        await _queue.Writer.WriteAsync(audioBytes);
    }

    /// <summary>Interrupt current playback and clear the queue.</summary>
    public void Interrupt()
    {
        // Drain the queue
        while (_queue.Reader.TryRead(out _)) { }
        _logger.LogDebug("Audio player interrupted");
    }

    private async Task PlaybackLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var audioBytes in _queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    IsPlaying = true;
                    await PlayAudioAsync(audioBytes, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Audio playback error");
                }
                finally
                {
                    IsPlaying = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private static async Task PlayAudioAsync(byte[] audioBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream(audioBytes);
        using var reader = new WaveFileReader(ms);
        using var waveOut = new WaveOutEvent();

        var tcs = new TaskCompletionSource();
        waveOut.PlaybackStopped += (_, _) => tcs.TrySetResult();

        waveOut.Init(reader);
        waveOut.Play();

        // Wait for playback to finish or cancellation
        using var reg = ct.Register(() => { waveOut.Stop(); tcs.TrySetCanceled(); });
        await tcs.Task;
    }

    public void Dispose()
    {
        Stop();
    }
}
