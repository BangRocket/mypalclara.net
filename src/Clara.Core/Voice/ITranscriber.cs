namespace Clara.Core.Voice;

/// <summary>Speech-to-text transcription.</summary>
public interface ITranscriber
{
    Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken ct = default);
}
