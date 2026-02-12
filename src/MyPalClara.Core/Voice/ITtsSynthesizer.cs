namespace MyPalClara.Core.Voice;

/// <summary>Text-to-speech synthesis.</summary>
public interface ITtsSynthesizer
{
    Task<byte[]?> SynthesizeAsync(string text, CancellationToken ct = default);
}
