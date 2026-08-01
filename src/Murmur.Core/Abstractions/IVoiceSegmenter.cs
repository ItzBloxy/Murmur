namespace Murmur.Core.Abstractions;

/// <summary>
/// Accumulates audio and detects phrase boundaries at silences, so the overlay can show a
/// running preview. Also retains the complete buffer for the final full-context pass.
/// </summary>
public interface IVoiceSegmenter
{
    /// <summary>Emits a completed phrase, as 16 kHz mono float32 samples.</summary>
    event Action<float[]>? SegmentCompleted;

    void Reset();
    void Push(float[] samples);

    /// <summary>Everything captured since the last <see cref="Reset"/>.</summary>
    float[] FullBuffer { get; }
}
