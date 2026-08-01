using Murmur.Core.Abstractions;
using Whisper.net;

namespace Murmur.Platform;

/// <summary>
/// Detects phrase boundaries so the overlay can show a running preview, while retaining the
/// complete buffer for the final full-context pass.
///
/// VAD runs over the <em>unconsumed tail</em> only — the audio since the last committed
/// boundary — and that tail is capped at <see cref="MaxTailSamples"/>. Detection cost per
/// pass is therefore bounded no matter how long the user talks or how rarely they pause.
/// </summary>
public sealed class SileroVoiceSegmenter : IVoiceSegmenter, IDisposable
{
    private const int SampleRate = 16000;

    /// <summary>Run detection at most this often, to avoid burning CPU on every 100ms frame.</summary>
    private const int AnalyzeEverySamples = SampleRate / 2;

    /// <summary>
    /// Hard cap on how much audio a single detection pass examines. Without it, continuous
    /// speech with no qualifying pause grows the tail without bound and every pass re-scans
    /// the whole thing. Preview is best-effort, so dropping the excess is acceptable — the
    /// final pass reads <see cref="FullBuffer"/>, which is never trimmed.
    /// </summary>
    private const int MaxTailSamples = SampleRate * 30;

    private readonly WhisperVadFactory _factory;
    private readonly WhisperVadProcessor _processor;
    private readonly int _silenceMs;
    private readonly List<float> _all = [];
    private readonly Lock _sync = new();

    private int _consumed;
    private int _samplesSinceAnalysis;

    public SileroVoiceSegmenter(string vadModelPath, int silenceMs)
    {
        _silenceMs = silenceMs;
        _factory = WhisperVadFactory.FromPath(vadModelPath);

        _processor = _factory.CreateBuilder()
            // Silero is tiny and runs on this CPU in well under a millisecond per second of
            // audio. Keeping it off the GPU leaves all the VRAM for Whisper.
            .WithUseGpu(false)
            .WithMinSilenceDuration(TimeSpan.FromMilliseconds(silenceMs))
            .WithMinSpeechDuration(TimeSpan.FromMilliseconds(200))
            .Build();
    }

    public event Action<float[]>? SegmentCompleted;

    public float[] FullBuffer
    {
        get { lock (_sync) return [.. _all]; }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _all.Clear();
            _consumed = 0;
            _samplesSinceAnalysis = 0;
            _processor.ResetState();
        }
    }

    public void Push(float[] samples)
    {
        float[]? completed = null;

        lock (_sync)
        {
            _all.AddRange(samples);
            _samplesSinceAnalysis += samples.Length;

            if (_samplesSinceAnalysis < AnalyzeEverySamples) return;
            _samplesSinceAnalysis = 0;

            completed = TryTakeCompletedPhrase();
        }

        if (completed is not null) SegmentCompleted?.Invoke(completed);
    }

    /// <summary>
    /// Runs detection across the entire buffer. This is the gate that keeps Whisper from
    /// inventing subtitle credits over silence — see <see cref="IVoiceSegmenter"/>.
    /// </summary>
    public float[] UnconsumedTail
    {
        get
        {
            lock (_sync)
            {
                var length = _all.Count - _consumed;
                if (length <= 0) return [];

                var tail = new float[length];
                _all.CopyTo(_consumed, tail, 0, length);
                return tail;
            }
        }
    }

    public bool ContainsSpeech(float[] samples)
    {
        if (samples.Length == 0) return false;

        lock (_sync)
        {
            _processor.ResetState();
            var speech = _processor.DetectSpeech(samples);
            _processor.ResetState();

            return speech.Count > 0;
        }
    }

    /// <summary>
    /// Returns a phrase if the tail contains speech that has been followed by enough
    /// silence to consider it finished. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private float[]? TryTakeCompletedPhrase()
    {
        var tailLength = _all.Count - _consumed;
        if (tailLength <= 0) return null;

        // Continuous speech with no qualifying pause, or a long uninterrupted silence,
        // would otherwise grow the tail forever and re-scan all of it every pass.
        if (tailLength > MaxTailSamples)
        {
            _consumed += tailLength - MaxTailSamples;
            tailLength = MaxTailSamples;
        }

        var tail = new float[tailLength];
        _all.CopyTo(_consumed, tail, 0, tailLength);

        _processor.ResetState();
        var speech = _processor.DetectSpeech(tail);
        if (speech.Count == 0) return null;

        var last = speech[^1];
        var tailDuration = TimeSpan.FromSeconds((double)tailLength / SampleRate);
        var silenceAfter = tailDuration - last.End;

        if (silenceAfter < TimeSpan.FromMilliseconds(_silenceMs)) return null;

        var phraseEnd = (int)(last.End.TotalSeconds * SampleRate);
        if (phraseEnd <= 0 || phraseEnd > tailLength) return null;

        var phrase = new float[phraseEnd];
        Array.Copy(tail, phrase, phraseEnd);
        _consumed += phraseEnd;

        return phrase;
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
    }
}
