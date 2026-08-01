using Murmur.Core;
using Murmur.Core.Abstractions;
using Murmur.Platform;

namespace Murmur.Integration.Tests;

[Trait("Category", "Integration")]
[Collection(WhisperGpuCollection.Name)]
public sealed class WhisperEngineTests : IAsyncLifetime
{
    private WhisperEngine _engine = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var store = new ModelStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur", "models"));

        _engine = new WhisperEngine(store, device: "cuda");
        await _engine.EnsureModelsDownloadedAsync(progress: null, Ct);
    }

    public async ValueTask DisposeAsync() => await _engine.DisposeAsync();

    [Fact]
    public async Task Loads_and_reports_loaded()
    {
        await _engine.EnsureLoadedAsync(Ct);

        Assert.True(_engine.IsLoaded);
    }

    [Fact]
    public async Task Unload_frees_the_model()
    {
        await _engine.EnsureLoadedAsync(Ct);

        await _engine.UnloadAsync();

        Assert.False(_engine.IsLoaded);
    }

    [Fact]
    public async Task Load_unload_reload_works()
    {
        await _engine.EnsureLoadedAsync(Ct);
        await _engine.UnloadAsync();
        await _engine.EnsureLoadedAsync(Ct);

        Assert.True(_engine.IsLoaded);
    }

    [Fact]
    public void Pin_and_unpin_toggle_the_flag()
    {
        _engine.Pin();
        Assert.True(_engine.IsPinned);

        _engine.Unpin();
        Assert.False(_engine.IsPinned);
    }

    /// <summary>
    /// Danish silence produces a subtitle-credit line, which the blocklist removes.
    /// </summary>
    [Fact]
    public async Task Danish_silence_artifacts_are_filtered_out()
    {
        await _engine.EnsureLoadedAsync(Ct);
        var filter = new HallucinationFilter();

        var segments = await _engine.TranscribeAsync(
            AudioFixtures.Silence(seconds: 4), "da", TranscriptionQuality.Final, Ct);

        Assert.Equal("", filter.Join(segments));
    }

    /// <summary>
    /// Documents why the VAD gate exists, and fails loudly if that stops being true.
    ///
    /// Whisper does not return empty for digital silence — it invents text and reports
    /// NoSpeechProbability of exactly 0.0 while doing so. English silence yields
    /// "Thank you.", which cannot be blocklisted because it is also ordinary dictation.
    /// The only workable defence is refusing to transcribe buffers with no detected
    /// speech, which <c>DictationController</c> does via <c>IVoiceSegmenter.ContainsSpeech</c>.
    /// </summary>
    [Fact]
    public async Task Silence_is_confidently_hallucinated_which_is_why_the_vad_gate_exists()
    {
        await _engine.EnsureLoadedAsync(Ct);

        var segments = await _engine.TranscribeAsync(
            AudioFixtures.Silence(seconds: 4), "en", TranscriptionQuality.Final, Ct);

        Assert.NotEmpty(segments);
        Assert.All(segments, s => Assert.True(
            s.NoSpeechProbability < HallucinationFilter.NoSpeechThreshold,
            $"Whisper reported noSpeech={s.NoSpeechProbability:F4}. If this now exceeds the " +
            "threshold, the model's own signal has become usable and layer 2 could be relied on."));
    }

    [Fact]
    public async Task Transcribes_danish_within_the_word_error_tolerance()
    {
        Assert.SkipUnless(AudioFixtures.Exists("danish.wav"), "Record fixtures first (Task 18).");
        await _engine.EnsureLoadedAsync(Ct);

        var segments = await _engine.TranscribeAsync(
            AudioFixtures.ReadWav("danish.wav"), "da", TranscriptionQuality.Final, Ct);

        var actual = new HallucinationFilter().Join(segments);
        var expected = await File.ReadAllTextAsync(AudioFixtures.Path("danish.txt"), Ct);

        Assert.True(WordErrorRate.Compute(expected, actual) <= 0.15,
            $"WER too high. Expected \"{expected}\", got \"{actual}\".");
    }

    [Fact]
    public async Task Transcribes_english_within_the_word_error_tolerance()
    {
        Assert.SkipUnless(AudioFixtures.Exists("english.wav"), "Record fixtures first (Task 18).");
        await _engine.EnsureLoadedAsync(Ct);

        var segments = await _engine.TranscribeAsync(
            AudioFixtures.ReadWav("english.wav"), "en", TranscriptionQuality.Final, Ct);

        var actual = new HallucinationFilter().Join(segments);
        var expected = await File.ReadAllTextAsync(AudioFixtures.Path("english.txt"), Ct);

        Assert.True(WordErrorRate.Compute(expected, actual) <= 0.10,
            $"WER too high. Expected \"{expected}\", got \"{actual}\".");
    }

    [Fact]
    public async Task Concurrent_transcriptions_are_serialized_not_corrupted()
    {
        await _engine.EnsureLoadedAsync(Ct);

        var a = _engine.TranscribeAsync(AudioFixtures.Silence(1), "da", TranscriptionQuality.Preview, Ct);
        var b = _engine.TranscribeAsync(AudioFixtures.Silence(1), "da", TranscriptionQuality.Preview, Ct);

        await Task.WhenAll(a, b);   // must not throw or deadlock
    }

    [Fact]
    public async Task Unload_during_transcription_waits_rather_than_freeing_the_context()
    {
        await _engine.EnsureLoadedAsync(Ct);

        var transcribe = _engine.TranscribeAsync(AudioFixtures.Silence(3), "da", TranscriptionQuality.Final, Ct);
        var unload = _engine.UnloadAsync();
        await Task.WhenAll(transcribe, unload);

        Assert.False(_engine.IsLoaded);
    }
}
