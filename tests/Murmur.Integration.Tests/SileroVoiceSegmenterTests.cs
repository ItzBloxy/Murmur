using Murmur.Platform;

namespace Murmur.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class SileroVoiceSegmenterTests : IAsyncLifetime
{
    private WhisperEngine _engine = null!;
    private SileroVoiceSegmenter _segmenter = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var store = new ModelStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur", "models"));
        _engine = new WhisperEngine(store, "cuda");
        await _engine.EnsureModelsDownloadedAsync(progress: null, Ct);
        _segmenter = new SileroVoiceSegmenter(_engine.VadModelPath, silenceMs: 500);
    }

    public async ValueTask DisposeAsync()
    {
        _segmenter.Dispose();
        await _engine.DisposeAsync();
    }

    [Fact]
    public void FullBuffer_accumulates_everything_pushed()
    {
        _segmenter.Reset();

        _segmenter.Push(new float[1600]);
        _segmenter.Push(new float[3200]);

        Assert.Equal(4800, _segmenter.FullBuffer.Length);
    }

    [Fact]
    public void Reset_clears_the_buffer()
    {
        _segmenter.Push(new float[1600]);

        _segmenter.Reset();

        Assert.Empty(_segmenter.FullBuffer);
    }

    [Fact]
    public void Pure_silence_emits_no_segments()
    {
        var emitted = 0;
        _segmenter.Reset();
        _segmenter.SegmentCompleted += _ => emitted++;

        for (var i = 0; i < 20; i++) _segmenter.Push(AudioFixtures.Silence(0.5));

        Assert.Equal(0, emitted);
    }

    /// <summary>
    /// The guarantee that keeps Whisper's silence hallucinations out of the user's textbox.
    /// Whisper itself returns "Thank you." for this input with full confidence, so this
    /// gate is the only thing standing between digital silence and inserted junk.
    /// </summary>
    [Fact]
    public void ContainsSpeech_is_false_for_pure_silence()
    {
        _segmenter.Reset();

        _segmenter.Push(AudioFixtures.Silence(4));

        Assert.False(_segmenter.ContainsSpeech());
    }

    [Fact]
    public void ContainsSpeech_is_false_for_an_empty_buffer()
    {
        _segmenter.Reset();

        Assert.False(_segmenter.ContainsSpeech());
    }

    /// <summary>
    /// The analysis tail is capped at 30s, but that cap must only ever bound preview work.
    /// The final pass reads FullBuffer, so trimming it would silently truncate the user's
    /// dictation — the worst possible failure here.
    /// </summary>
    [Fact]
    public void FullBuffer_is_never_trimmed_by_the_tail_cap()
    {
        _segmenter.Reset();

        for (var i = 0; i < 8; i++) _segmenter.Push(AudioFixtures.Silence(5));   // 40s, past the cap

        Assert.Equal(AudioFixtures.SampleRate * 40, _segmenter.FullBuffer.Length);
    }

    [Fact]
    public void Speech_followed_by_silence_emits_one_segment()
    {
        Assert.SkipUnless(AudioFixtures.Exists("danish.wav"), "Record fixtures first (Task 18).");
        var emitted = new List<float[]>();
        _segmenter.Reset();
        _segmenter.SegmentCompleted += emitted.Add;

        _segmenter.Push(AudioFixtures.ReadWav("danish.wav"));
        _segmenter.Push(AudioFixtures.Silence(1.5));   // trailing silence closes the phrase

        Assert.NotEmpty(emitted);
        Assert.All(emitted, s => Assert.True(s.Length > 0));
    }

    [Fact]
    public void ContainsSpeech_is_true_for_real_speech()
    {
        Assert.SkipUnless(AudioFixtures.Exists("danish.wav"), "Record fixtures first (Task 18).");
        _segmenter.Reset();

        _segmenter.Push(AudioFixtures.ReadWav("danish.wav"));

        Assert.True(_segmenter.ContainsSpeech());
    }
}
