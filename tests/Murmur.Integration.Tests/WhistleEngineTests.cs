using Murmur.Core;
using Murmur.Core.Abstractions;
using Murmur.Platform;

namespace Murmur.Integration.Tests;

/// <summary>
/// Runs the real needle.exe against the real Whistle model, downloading both (~18 MB) into
/// the same models folder the app uses.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WhistleEngineTests : IAsyncLifetime
{
    private WhistleEngine _engine = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var store = new ModelStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur", "models"));

        _engine = new WhistleEngine(store);
        await _engine.EnsureDownloadedAsync(progress: null, Ct);
    }

    public async ValueTask DisposeAsync() => await _engine.DisposeAsync();

    [Fact]
    public void Downloads_both_files()
    {
        Assert.False(_engine.NeedsDownload);
    }

    /// <summary>Nothing stays resident between calls, so the tray must not claim a loaded model.</summary>
    [Fact]
    public async Task Never_reports_a_resident_model()
    {
        await _engine.EnsureLoadedAsync(Ct);

        Assert.False(_engine.IsLoaded);
    }

    /// <summary>Unlike Whisper, Whistle does not invent subtitle credits for silence.</summary>
    [Fact]
    public async Task Silence_transcribes_to_nothing()
    {
        var segments = await _engine.TranscribeAsync(
            AudioFixtures.Silence(seconds: 4), "de", TranscriptionQuality.Final, Ct);

        Assert.Equal("", new HallucinationFilter().Join(segments));
    }

    /// <summary>
    /// Proves the whole path — WAV encoding, the pipe, the process and the JSON — carries
    /// real speech. Synthesized because the recorded fixtures are optional; English because
    /// it is the one Windows voice every install has.
    /// </summary>
    [Fact]
    public async Task Transcribes_synthesized_speech()
    {
        const string spoken = "Please send the quarterly report to the finance team before Friday afternoon.";

        var segments = await _engine.TranscribeAsync(
            SynthesizedSpeech.Render(spoken), "en", TranscriptionQuality.Final, Ct);

        var actual = new HallucinationFilter().Join(segments);
        Assert.True(WordErrorRate.Compute(spoken, actual) <= 0.10,
            $"WER too high. Expected \"{spoken}\", got \"{actual}\".");
    }

    [Fact]
    public async Task Cancellation_stops_the_engine()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _engine.TranscribeAsync(
            AudioFixtures.Silence(seconds: 2), "de", TranscriptionQuality.Preview, cancelled.Token));
    }

    [Fact]
    public async Task Transcribes_german_within_the_word_error_tolerance()
    {
        Assert.SkipUnless(AudioFixtures.Exists("german.wav"), "Record german.wav and german.txt first (see fixtures/README.md).");

        var segments = await _engine.TranscribeAsync(
            AudioFixtures.ReadWav("german.wav"), "de", TranscriptionQuality.Final, Ct);

        var actual = new HallucinationFilter().Join(segments);
        var expected = await File.ReadAllTextAsync(AudioFixtures.Path("german.txt"), Ct);

        Assert.True(WordErrorRate.Compute(expected, actual) <= 0.15,
            $"WER too high. Expected \"{expected}\", got \"{actual}\".");
    }
}

public sealed class WhistleEngineDownloadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "murmur-models", Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public WhistleEngineDownloadTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Needs_a_download_until_both_files_are_cached()
    {
        await using var engine = new WhistleEngine(new ModelStore(_dir));
        Assert.True(engine.NeedsDownload);

        await File.WriteAllBytesAsync(Path.Combine(_dir, WhistleEngine.EngineFileName), new byte[2048], Ct);
        Assert.True(engine.NeedsDownload);

        await File.WriteAllBytesAsync(Path.Combine(_dir, WhistleEngine.ModelFileName), new byte[2048], Ct);
        Assert.False(engine.NeedsDownload);
    }

    /// <summary>The pinned commit is in the name, so bumping the pin cannot reuse a stale file.</summary>
    [Fact]
    public void Cached_file_names_carry_the_pinned_revision()
    {
        Assert.Matches("^needle-[0-9a-f]{8}\\.exe$", WhistleEngine.EngineFileName);
        Assert.Matches("^whistle-[0-9a-f]{8}\\.cact$", WhistleEngine.ModelFileName);
    }
}
