using System.Diagnostics;
using Murmur.Core.Abstractions;
using Murmur.Platform;

namespace Murmur.Integration.Tests;

/// <summary>
/// Asserts that transcription actually runs on the GPU.
///
/// It silently did not for the entire early life of this project. Whisper.net's CUDA
/// package is built against CUDA 13; this machine has a 12.x driver and no toolkit, so the
/// loader reported "not available" and quietly returned the CPU library. That is not an
/// exception, so the try/catch fallback in WhisperEngine never fired and every log line
/// claimed "cuda" while a 1.4-second utterance took 9.6 seconds.
///
/// The cost was roughly 40x. Nothing else in the suite would notice.
/// </summary>
[Trait("Category", "Integration")]
[Collection(WhisperGpuCollection.Name)]
public sealed class BackendSpeedCheck
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Log(string message) => TestContext.Current.TestOutputHelper?.WriteLine(message);

    [Fact]
    public async Task Transcription_runs_on_a_gpu_backend_not_the_cpu()
    {
        var store = new ModelStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur", "models"));

        await using var engine = new WhisperEngine(store, "cuda");
        await engine.EnsureModelsDownloadedAsync(null, Ct);

        var loadWatch = Stopwatch.StartNew();
        await engine.EnsureLoadedAsync(Ct);
        loadWatch.Stop();

        Log($"backend    : {engine.LoadedBackend}");
        Log($"model load : {loadWatch.Elapsed.TotalSeconds:F2}s");

        // Noise rather than silence: silence decodes to almost nothing and would hide the
        // encoder cost, which is what actually dominates a short dictation.
        var random = new Random(1234);
        var audio = new float[16000 * 12];
        for (var i = 0; i < audio.Length; i++) audio[i] = (float)(random.NextDouble() * 0.2 - 0.1);

        await engine.TranscribeAsync(audio, "en", TranscriptionQuality.Final, Ct);   // warm

        var timings = new List<double>();
        for (var run = 0; run < 3; run++)
        {
            var watch = Stopwatch.StartNew();
            await engine.TranscribeAsync(audio, "en", TranscriptionQuality.Final, Ct);
            watch.Stop();
            timings.Add(watch.Elapsed.TotalSeconds);
        }

        timings.Sort();
        var median = timings[1];
        var seconds = audio.Length / 16000.0;

        Log($"audio      : {seconds:F2}s");
        Log($"median     : {median:F2}s  (rtf {median / seconds:F3})");

        Assert.False(engine.LoadedBackend.Equals("Cpu", StringComparison.OrdinalIgnoreCase),
            $"Whisper loaded the '{engine.LoadedBackend}' backend. The GPU runtime's native " +
            "dependencies are missing, so transcription is running on the CPU at roughly 40x " +
            "the latency. Check the Whisper.net.Runtime.* packages against the installed driver.");

        // Generous: a GPU manages ~0.25s here, the CPU took ~10s. This only catches a
        // collapse back to CPU-class performance, not ordinary variation.
        Assert.True(median < 3.0,
            $"transcribing {seconds:F1}s took {median:F2}s — that is CPU-class latency.");
    }
}
