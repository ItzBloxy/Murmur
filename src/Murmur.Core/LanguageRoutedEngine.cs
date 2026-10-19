using Murmur.Core.Abstractions;

namespace Murmur.Core;

/// <summary>
/// Presents Whisper and Whistle as one engine, picking by language, so
/// <see cref="DictationController"/> never needs to know there are two.
///
/// Moving to the other model unloads the one being left: Whisper holds ~1.5 GB of VRAM,
/// and keeping it resident while dictating German would be the opposite of what the user
/// chose. The pin from "Preload model" is the user's intent rather than either engine's,
/// so it lives here and follows the switch.
/// </summary>
public sealed class LanguageRoutedEngine(
    ITranscriptionEngine whisper,
    ITranscriptionEngine whistle,
    Func<string> currentLanguage,
    IDiagnosticLog? log = null) : ITranscriptionEngine
{
    private readonly IDiagnosticLog _log = log ?? NullDiagnosticLog.Instance;
    private readonly SemaphoreSlim _switch = new(1, 1);

    /// <summary>The engine last used, or null before the first call.</summary>
    private ITranscriptionEngine? _active;
    private bool _pinned;

    public SpeechModel ActiveModel => SpeechLanguages.ModelFor(currentLanguage());

    public bool IsLoaded => EngineFor(currentLanguage()).IsLoaded;
    public bool IsPinned => _pinned;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        var engine = await ActivateAsync(EngineFor(currentLanguage()), cancellationToken);
        await engine.EnsureLoadedAsync(cancellationToken);
    }

    /// <summary>
    /// Called when the language setting changes, so the model being left is released at
    /// once rather than at the next dictation. Preloads the new one if the user pinned.
    /// </summary>
    public async Task SwitchToCurrentLanguageAsync(CancellationToken cancellationToken = default)
    {
        var engine = await ActivateAsync(EngineFor(currentLanguage()), cancellationToken);
        if (_pinned) await engine.EnsureLoadedAsync(cancellationToken);
    }

    /// <summary>Idle unload and "Release GPU now" both mean everything, not just the active engine.</summary>
    public async Task UnloadAsync()
    {
        await whisper.UnloadAsync();
        await whistle.UnloadAsync();
    }

    public void Pin()
    {
        _pinned = true;
        EngineFor(currentLanguage()).Pin();
    }

    public void Unpin()
    {
        _pinned = false;
        whisper.Unpin();
        whistle.Unpin();
    }

    /// <summary>Routes by the language passed, not the setting — they differ if it changed mid-dictation.</summary>
    public async Task<IReadOnlyList<TranscribedSegment>> TranscribeAsync(
        float[] samples,
        string language,
        TranscriptionQuality quality,
        CancellationToken cancellationToken = default)
    {
        var engine = await ActivateAsync(EngineFor(language), cancellationToken);
        return await engine.TranscribeAsync(samples, language, quality, cancellationToken);
    }

    private ITranscriptionEngine EngineFor(string language) =>
        SpeechLanguages.ModelFor(language) == SpeechModel.Whistle ? whistle : whisper;

    private async Task<ITranscriptionEngine> ActivateAsync(
        ITranscriptionEngine engine, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(_active, engine)) return engine;

        await _switch.WaitAsync(cancellationToken);
        try
        {
            if (ReferenceEquals(_active, engine)) return engine;

            var previous = _active;
            _active = engine;

            if (_pinned) engine.Pin();

            if (previous is not null)
            {
                previous.Unpin();
                // WhisperEngine.UnloadAsync waits out a transcription already in flight, so a
                // preview still running in the old model finishes before its context is freed.
                await previous.UnloadAsync();
                _log.Write("model", $"switched to {NameOf(engine)}, released {NameOf(previous)}");
            }

            return engine;
        }
        finally
        {
            _switch.Release();
        }
    }

    private string NameOf(ITranscriptionEngine engine) =>
        ReferenceEquals(engine, whistle) ? nameof(SpeechModel.Whistle) : nameof(SpeechModel.Whisper);

    public async ValueTask DisposeAsync()
    {
        await whisper.DisposeAsync();
        await whistle.DisposeAsync();
        _switch.Dispose();
    }
}
