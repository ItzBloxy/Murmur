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

    /// <summary>
    /// Whether the full buffer contains any detected speech at all.
    ///
    /// This is the gate that stops Whisper hallucinating on silence, and it is the only
    /// one that works: measured against large-v3-turbo, four seconds of digital silence
    /// produces "Thank you." (en) and a subtitle-credit line (da), both reported with
    /// NoSpeechProbability of exactly 0.0 — so Whisper's own confidence signal offers no
    /// protection whatsoever. Runs detection over the buffer, so call it once at stop.
    /// </summary>
    bool ContainsSpeech();
}
