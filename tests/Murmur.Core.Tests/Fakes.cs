using Murmur.Core;
using Murmur.Core.Abstractions;

namespace Murmur.Core.Tests;

public sealed class FakeAudioCapture : IAudioCapture
{
    public event Action<float[]>? FramesAvailable;
    public event Action<string>? Failed;

    public bool IsCapturing { get; private set; }
    public int StartCount { get; private set; }
    public Exception? ThrowOnStart { get; set; }

    public void Start()
    {
        if (ThrowOnStart is not null) throw ThrowOnStart;
        IsCapturing = true;
        StartCount++;
    }

    public void Stop() => IsCapturing = false;

    public void EmitFrames(float[] samples) => FramesAvailable?.Invoke(samples);
    public void EmitFailure(string message) => Failed?.Invoke(message);

    public void Dispose() { }
}

public sealed class FakeVoiceSegmenter : IVoiceSegmenter
{
    private readonly List<float> _buffer = [];

    public event Action<float[]>? SegmentCompleted;

    public int ResetCount { get; private set; }
    public float[] FullBuffer => [.. _buffer];

    public void Reset()
    {
        _buffer.Clear();
        ResetCount++;
    }

    public void Push(float[] samples) => _buffer.AddRange(samples);

    public void EmitSegment(float[] samples) => SegmentCompleted?.Invoke(samples);
}

public sealed class FakeTranscriptionEngine : ITranscriptionEngine
{
    private readonly List<(TranscriptionQuality Quality, int SampleCount)> _calls = [];
    private readonly Lock _sync = new();

    public bool IsLoaded { get; private set; }
    public bool IsPinned { get; private set; }
    public int LoadCount { get; private set; }
    public int UnloadCount { get; private set; }
    public Exception? ThrowOnLoad { get; set; }
    public string? LastLanguage { get; private set; }

    public IReadOnlyList<(TranscriptionQuality Quality, int SampleCount)> Calls
    {
        get { lock (_sync) return [.. _calls]; }
    }

    /// <summary>Result returned per quality level. Defaults to a single plain segment.</summary>
    public Func<TranscriptionQuality, IReadOnlyList<TranscribedSegment>> Results { get; set; } =
        quality => [new TranscribedSegment(quality == TranscriptionQuality.Final ? "final text" : "preview", 0.05f)];

    /// <summary>Set to block a transcription until the test releases it.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnLoad is not null) return Task.FromException(ThrowOnLoad);
        if (!IsLoaded) LoadCount++;
        IsLoaded = true;
        return Task.CompletedTask;
    }

    public Task UnloadAsync()
    {
        if (IsLoaded) UnloadCount++;
        IsLoaded = false;
        return Task.CompletedTask;
    }

    public void Pin() => IsPinned = true;
    public void Unpin() => IsPinned = false;

    public async Task<IReadOnlyList<TranscribedSegment>> TranscribeAsync(
        float[] samples, string language, TranscriptionQuality quality, CancellationToken cancellationToken = default)
    {
        lock (_sync) _calls.Add((quality, samples.Length));
        LastLanguage = language;

        if (Gate is not null) await Gate.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return Results(quality);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeTextInjector : ITextInjector
{
    public nint ForegroundWindow { get; set; } = 1;
    public InjectionResult NextResult { get; set; } = InjectionResult.Injected;
    public List<(string Text, nint ExpectedWindow)> Injections { get; } = [];

    public nint GetForegroundWindow() => ForegroundWindow;

    public InjectionResult Inject(string text, nint expectedWindow)
    {
        Injections.Add((text, expectedWindow));
        return NextResult;
    }
}

public sealed class FakeOverlay : IOverlay
{
    public bool IsVisible { get; private set; }
    public string LastPreview { get; private set; } = "";
    public bool TranscribingShown { get; private set; }

    public void Show() { IsVisible = true; TranscribingShown = false; }
    public void SetPreview(string text) => LastPreview = text;
    public void SetTranscribing() => TranscribingShown = true;
    public void Hide() => IsVisible = false;
}

public sealed class FakeNotifier : INotifier
{
    public List<string> Messages { get; } = [];
    public DictationState LastState { get; private set; } = DictationState.Idle;
    public bool LastModelLoaded { get; private set; }
    public bool LastModelPinned { get; private set; }

    public void Notify(string title, string message) => Messages.Add(message);

    public void SetState(DictationState state, bool modelLoaded, bool modelPinned)
    {
        LastState = state;
        LastModelLoaded = modelLoaded;
        LastModelPinned = modelPinned;
    }
}
