using Murmur.Core;
using Murmur.Core.Abstractions;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace Murmur.Platform;

/// <summary>
/// Wraps Whisper.net and owns the model lifecycle.
///
/// whisper.cpp contexts are not thread-safe, so every call — including unload — is
/// serialized through <see cref="_gate"/>. A Release GPU pressed mid-transcription
/// therefore waits for the pass to finish rather than freeing the context under it.
/// </summary>
public sealed class WhisperEngine(ModelStore store, string device, IDiagnosticLog? log = null)
    : ITranscriptionEngine
{
    private readonly IDiagnosticLog _log = log ?? NullDiagnosticLog.Instance;

    public const string WhisperModelFileName = "ggml-large-v3-turbo-q8_0.bin";
    public const string VadModelFileName = "ggml-silero-v6.2.0.bin";

    private readonly SemaphoreSlim _gate = new(1, 1);

    private WhisperFactory? _factory;
    private string? _whisperModelPath;
    private string? _vadModelPath;

    public bool IsLoaded => _factory is not null;
    public bool IsPinned { get; private set; }

    /// <summary>Set once a GPU load threw and we retried on CPU for this session.</summary>
    public bool UsingCpuFallback { get; private set; }

    /// <summary>
    /// The backend whisper.cpp actually loaded, which is not the one we asked for whenever
    /// the GPU runtime is missing its dependencies. That case is not an exception — the
    /// loader reports "not available" and quietly returns the CPU library — so it has to be
    /// read back rather than inferred from a try/catch.
    /// </summary>
    public string LoadedBackend { get; private set; } = "not loaded";

    public string VadModelPath =>
        _vadModelPath ?? throw new InvalidOperationException("Call EnsureModelsDownloadedAsync first.");

    public void Pin() => IsPinned = true;
    public void Unpin() => IsPinned = false;

    /// <summary>Fetches both models if they are not already cached. Safe to call repeatedly.</summary>
    public async Task EnsureModelsDownloadedAsync(IProgress<long>? progress, CancellationToken cancellationToken)
    {
        _whisperModelPath = await store.EnsureAsync(
            WhisperModelFileName,
            ct => WhisperGgmlDownloader.Default.GetGgmlModelAsync(
                GgmlType.LargeV3Turbo, QuantizationType.Q8_0, ct),
            progress,
            cancellationToken);

        _vadModelPath = await store.EnsureAsync(
            VadModelFileName,
            ct => WhisperGgmlDownloader.Default.GetGgmlSileroVadModelAsync(SileroVadType.V6_2_0, ct),
            progress: null,
            cancellationToken);
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoaded) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsLoaded) return;

            if (_whisperModelPath is null)
            {
                await EnsureModelsDownloadedAsync(progress: null, cancellationToken);
            }

            var preferGpu = !device.Equals("cpu", StringComparison.OrdinalIgnoreCase) && !UsingCpuFallback;

            try
            {
                _factory = CreateFactory(_whisperModelPath!, preferGpu);
            }
            catch (Exception ex) when (preferGpu)
            {
                // VRAM exhausted by a game, or a driver fault. The 9800X3D makes CPU a real fallback.
                _log.Write("whisper", $"GPU load threw, retrying on CPU: {ex.Message}");
                UsingCpuFallback = true;
                _factory = CreateFactory(_whisperModelPath!, useGpu: false);
            }

            LoadedBackend = RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown";
            _log.Write("whisper", $"model loaded — backend={LoadedBackend} (requested {(preferGpu ? "gpu" : "cpu")})");

            if (preferGpu && LoadedBackend.Equals("Cpu", StringComparison.OrdinalIgnoreCase))
            {
                _log.Write("whisper",
                    "RUNNING ON CPU despite requesting the GPU — the GPU runtime's native dependencies " +
                    "are missing. Expect roughly 10s per 30s window instead of well under a second.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static WhisperFactory CreateFactory(string modelPath, bool useGpu)
    {
        var options = new WhisperFactoryOptions
        {
            UseGpu = useGpu,

            // An exact reformulation of attention, not an approximation — transcripts are
            // character-identical. Measured 1.5-1.9x on GPU and ~1.3x on CPU.
            UseFlashAttention = true,
        };

        return WhisperFactory.FromPath(modelPath, options);
    }

    public async Task UnloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _factory?.Dispose();
            _factory = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TranscribedSegment>> TranscribeAsync(
        float[] samples,
        string language,
        TranscriptionQuality quality,
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var factory = _factory ?? throw new InvalidOperationException("Model is not loaded.");

            var builder = factory.CreateBuilder()
                .WithLanguage(language)
                .WithNoSpeechThreshold(HallucinationFilter.NoSpeechThreshold)
                .WithProbabilities()
                // No effect on a GPU backend, but halves CPU time if we ever land there:
                // whisper.cpp defaults to 4 threads regardless of the machine.
                .WithThreads(Environment.ProcessorCount);

            builder = quality == TranscriptionQuality.Preview
                ? builder.WithGreedySamplingStrategy()
                : builder.WithBeamSearchSamplingStrategy(b => b.WithBeamSize(5));

            await using var processor = builder.Build();

            var results = new List<TranscribedSegment>();
            await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
            {
                results.Add(new TranscribedSegment(segment.Text, segment.NoSpeechProbability));
            }

            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await UnloadAsync();
        _gate.Dispose();
    }
}
