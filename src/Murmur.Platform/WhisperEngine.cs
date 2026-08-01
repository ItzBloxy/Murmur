using Murmur.Core;
using Murmur.Core.Abstractions;
using Whisper.net;
using Whisper.net.Ggml;

namespace Murmur.Platform;

/// <summary>
/// Wraps Whisper.net and owns the model lifecycle.
///
/// whisper.cpp contexts are not thread-safe, so every call — including unload — is
/// serialized through <see cref="_gate"/>. A Release GPU pressed mid-transcription
/// therefore waits for the pass to finish rather than freeing the context under it.
/// </summary>
public sealed class WhisperEngine(ModelStore store, string device) : ITranscriptionEngine
{
    public const string WhisperModelFileName = "ggml-large-v3-turbo-q8_0.bin";
    public const string VadModelFileName = "ggml-silero-v6.2.0.bin";

    private readonly SemaphoreSlim _gate = new(1, 1);

    private WhisperFactory? _factory;
    private string? _whisperModelPath;
    private string? _vadModelPath;

    public bool IsLoaded => _factory is not null;
    public bool IsPinned { get; private set; }

    /// <summary>Set once the CUDA path has failed and we have fallen back for this session.</summary>
    public bool UsingCpuFallback { get; private set; }

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

            var preferCuda = device.Equals("cuda", StringComparison.OrdinalIgnoreCase) && !UsingCpuFallback;

            try
            {
                _factory = CreateFactory(_whisperModelPath!, preferCuda);
            }
            catch (Exception) when (preferCuda)
            {
                // VRAM exhausted by a game, or a driver fault. The 9800X3D makes CPU a real fallback.
                UsingCpuFallback = true;
                _factory = CreateFactory(_whisperModelPath!, useCuda: false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static WhisperFactory CreateFactory(string modelPath, bool useCuda)
    {
        var options = new WhisperFactoryOptions
        {
            UseGpu = useCuda,
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
                .WithProbabilities();

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
