using System.Text;
using Murmur.Core.Abstractions;

namespace Murmur.Core;

/// <summary>
/// Owns the dictation lifecycle: Idle → Recording → Transcribing → Idle. Every dependency
/// is an interface, so this class — which holds all the ordering rules — is fully testable
/// without a microphone, a GPU or a window.
/// </summary>
public sealed class DictationController : IAsyncDisposable
{
    /// <summary>Below this, a speechless recording is treated as a mistimed gesture rather than intent.</summary>
    private const double MinimumPlausibleDictationSeconds = 1.5;

    private readonly IAudioCapture _audio;
    private readonly IVoiceSegmenter _segmenter;
    private readonly ITranscriptionEngine _engine;
    private readonly ITextInjector _injector;
    private readonly IOverlay _overlay;
    private readonly INotifier _notifier;
    private readonly Func<Settings> _settings;
    private readonly TimeProvider _time;
    private readonly IDiagnosticLog _log;

    private readonly GestureRecognizer _gestures;
    private readonly HallucinationFilter _filter = new();
    private readonly StringBuilder _preview = new();
    private readonly SemaphoreSlim _transition = new(1, 1);

    private ITimer? _maxDurationTimer;
    private ITimer? _idleTimer;
    private CancellationTokenSource? _previewCancellation;
    private Task _background = Task.CompletedTask;
    private nint _targetWindow;

    public DictationController(
        IAudioCapture audio,
        IVoiceSegmenter segmenter,
        ITranscriptionEngine engine,
        ITextInjector injector,
        IOverlay overlay,
        INotifier notifier,
        Func<Settings> settings,
        TimeProvider time,
        IDiagnosticLog? log = null)
    {
        _log = log ?? NullDiagnosticLog.Instance;
        _audio = audio;
        _segmenter = segmenter;
        _engine = engine;
        _injector = injector;
        _overlay = overlay;
        _notifier = notifier;
        _settings = settings;
        _time = time;
        _gestures = new GestureRecognizer(settings().HoldThresholdMs);

        _audio.FramesAvailable += OnFrames;
        _audio.Failed += OnAudioFailed;
        _segmenter.SegmentCompleted += OnSegmentCompleted;
    }

    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>Awaits work started on background paths. Tests use this; production does not need it.</summary>
    public Task WaitForIdleAsync() => _background;

    public async Task HandleKeyEventAsync(KeyTransition transition, long timestampMs)
    {
        var action = _gestures.Handle(transition, timestampMs);
        _log.Write("gesture", $"{transition} -> {action} (gesture state now {_gestures.State}, dictation {State})");

        switch (action)
        {
            case GestureAction.StartRecording:
                await StartRecordingAsync();
                break;
            case GestureAction.StopRecording:
                await StopRecordingAsync("");
                break;
        }
    }

    public async Task PreloadAsync()
    {
        try
        {
            await _engine.EnsureLoadedAsync();
            _engine.Pin();
            CancelIdleTimer();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _notifier.Notify("Murmur", $"Could not preload the model: {ex.Message}");
        }
    }

    public async Task ReleaseGpuAsync()
    {
        _engine.Unpin();
        CancelIdleTimer();
        await _engine.UnloadAsync();
        UpdateStatus();
    }

    /// <summary>Transcribes one completed phrase into the live overlay preview.</summary>
    public async Task HandleSegmentAsync(float[] samples)
    {
        if (State != DictationState.Recording) return;

        var cancellation = _previewCancellation;
        if (cancellation is null) return;

        try
        {
            var segments = await _engine.TranscribeAsync(
                samples, _settings().Language, TranscriptionQuality.Preview, cancellation.Token);

            var text = _filter.Join(segments);
            if (text.Length == 0 || State != DictationState.Recording) return;

            if (_preview.Length > 0) _preview.Append(' ');
            _preview.Append(text);
            _overlay.SetPreview(_preview.ToString());
        }
        catch (OperationCanceledException)
        {
            // Expected when the user stops mid-preview.
        }
        catch (Exception ex)
        {
            // Preview is best-effort. The final pass is what the user actually receives.
            _log.Write("preview", $"preview pass failed (ignored): {ex.Message}");
        }
    }

    private async Task StartRecordingAsync()
    {
        await _transition.WaitAsync();
        try
        {
            if (State != DictationState.Idle) return;

            _targetWindow = _injector.GetForegroundWindow();
            _log.Write("record", $"START — target window captured: 0x{_targetWindow:X}");

            _preview.Clear();
            _overlay.SetPreview("");
            _segmenter.Reset();

            try
            {
                _audio.Start();
            }
            catch (Exception ex)
            {
                _gestures.Reset();
                _log.Write("error", $"could not start audio capture: {ex}");
                _notifier.Notify("Murmur", $"Could not start recording: {ex.Message}");
                return;
            }

            State = DictationState.Recording;
            _previewCancellation = new CancellationTokenSource();
            _overlay.Show();
            CancelIdleTimer();
            UpdateStatus();

            _maxDurationTimer = _time.CreateTimer(
                _ => _background = StopRecordingAsync("Reached the maximum recording length."),
                null,
                TimeSpan.FromMinutes(_settings().MaxRecordingMinutes),
                Timeout.InfiniteTimeSpan);

            // Task.Run, not a bare call: loading the model is seconds of synchronous native
            // work, and this method runs inline on whichever thread delivered the hotkey.
            _background = Task.Run(LoadModelInBackgroundAsync);
        }
        finally
        {
            _transition.Release();
        }
    }

    private async Task LoadModelInBackgroundAsync()
    {
        try
        {
            await _engine.EnsureLoadedAsync();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _notifier.Notify("Murmur", $"Model failed to load: {ex.Message}");
        }
    }

    private async Task StopRecordingAsync(string reason)
    {
        await _transition.WaitAsync();
        try
        {
            // A late max-duration timer can arrive after a normal stop has already run.
            // Returning here must NOT fall into ReturnToIdle — that would hide the overlay
            // and restart the idle timer for a session that already ended.
            if (State != DictationState.Recording) return;

            try
            {
                CancelMaxDurationTimer();
                _audio.Stop();
                _previewCancellation?.Cancel();

                State = DictationState.Transcribing;
                _overlay.SetTranscribing();
                UpdateStatus();

                if (reason.Length > 0) _notifier.Notify("Murmur", reason);

                var buffer = _segmenter.FullBuffer;
                _log.Write("record", $"STOP — buffer {buffer.Length} samples ({buffer.Length / 16000.0:F2}s)");

                if (buffer.Length == 0)
                {
                    _log.Write("record", "nothing captured — no audio reached the segmenter");
                    return;
                }

                // Nothing was actually said. Whisper does not return empty for silence —
                // it confidently invents subtitle credits — so this gate, not the model's
                // own no-speech probability, is what keeps junk out of the user's textbox.
                var hasSpeech = _segmenter.ContainsSpeech();
                _log.Write("vad", $"speech detected in buffer: {hasSpeech}");

                if (!hasSpeech)
                {
                    _log.Write("vad", "NO SPEECH — skipping transcription. Check the audio peak above: " +
                                      "a near-zero peak means the wrong microphone or a muted one.");

                    // Staying silent is right when someone deliberately recorded nothing.
                    // But a recording this short is almost always a mistimed gesture, and
                    // saying nothing leaves the user with no idea why nothing happened.
                    var seconds = buffer.Length / 16000.0;
                    if (seconds < MinimumPlausibleDictationSeconds)
                    {
                        _notifier.Notify("Murmur",
                            $"That was only {seconds:F1}s — too short to contain speech. " +
                            "Hold the key while you talk, or tap once to start and again to stop.");
                    }

                    return;
                }

                await _engine.EnsureLoadedAsync();

                var started = _time.GetTimestamp();
                var segments = await _engine.TranscribeAsync(
                    buffer, _settings().Language, TranscriptionQuality.Final, CancellationToken.None);
                var elapsed = _time.GetElapsedTime(started);

                _log.Write("whisper", $"language={_settings().Language} took {elapsed.TotalSeconds:F2}s, " +
                                      $"{segments.Count} segment(s)");
                foreach (var segment in segments)
                {
                    _log.Write("whisper",
                        $"  noSpeech={segment.NoSpeechProbability:F4} " +
                        $"rejected={_filter.ShouldReject(segment.Text, segment.NoSpeechProbability)} " +
                        $"text=\"{segment.Text.Trim()}\"");
                }

                var text = _filter.Join(segments);
                if (text.Length == 0)
                {
                    _log.Write("filter", "everything was filtered out — nothing to insert");
                    return;
                }

                _log.Write("inject", $"inserting {text.Length} chars into 0x{_targetWindow:X}: \"{text}\"");
                var result = _injector.Inject(text, _targetWindow);
                _log.Write("inject", $"result: {result}");

                switch (result)
                {
                    case InjectionResult.CopiedToClipboardFocusChanged:
                        _notifier.Notify("Murmur", "Focus changed — the text was copied to your clipboard instead.");
                        break;
                    case InjectionResult.Failed:
                        _notifier.Notify("Murmur", "Could not insert text into the focused window.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.Write("error", $"transcription/insertion failed: {ex}");
                _notifier.Notify("Murmur", $"Transcription failed: {ex.Message}");
            }
            finally
            {
                ReturnToIdle();
            }
        }
        finally
        {
            _transition.Release();
        }
    }

    private void ReturnToIdle()
    {
        State = DictationState.Idle;

        // Deliberately not disposed here. A preview pass may still be observing this
        // token, and disposing a CancellationTokenSource out from under an awaiting
        // WaitAsync throws ObjectDisposedException. It is cheap to let go of and is
        // disposed properly in DisposeAsync.
        _previewCancellation = null;

        _overlay.Hide();
        UpdateStatus();
        StartIdleTimer();
    }

    private void OnFrames(float[] samples)
    {
        if (State == DictationState.Recording) _segmenter.Push(samples);
    }

    // Offloaded because this fires on the audio device's recording thread. Transcribing a
    // preview inline there would stall capture and drop the audio still being spoken.
    private void OnSegmentCompleted(float[] samples) => _background = Task.Run(() => HandleSegmentAsync(samples));

    private void OnAudioFailed(string message) => _background = AbortRecordingAsync(message);

    /// <summary>
    /// Aborts without transcribing — a half-captured recording must never be inserted.
    /// Takes the same lock as the normal stop path: this arrives on the audio callback
    /// thread and would otherwise race a concurrent stop for ownership of the state.
    /// </summary>
    private async Task AbortRecordingAsync(string message)
    {
        await _transition.WaitAsync();
        try
        {
            if (State != DictationState.Recording) return;

            CancelMaxDurationTimer();
            _audio.Stop();
            _previewCancellation?.Cancel();

            // The gesture recognizer still believes recording is latched on. Without this,
            // the next key press would be read as a stop rather than a fresh start.
            _gestures.Reset();

            _notifier.Notify("Murmur", $"Recording aborted: {message}");
            ReturnToIdle();
        }
        finally
        {
            _transition.Release();
        }
    }

    private void StartIdleTimer()
    {
        CancelIdleTimer();
        if (_engine.IsPinned || !_engine.IsLoaded) return;

        _idleTimer = _time.CreateTimer(
            _ => _background = UnloadIfStillIdleAsync(),
            null,
            TimeSpan.FromMinutes(_settings().IdleUnloadMinutes),
            Timeout.InfiniteTimeSpan);
    }

    private async Task UnloadIfStillIdleAsync()
    {
        if (State != DictationState.Idle || _engine.IsPinned) return;
        await _engine.UnloadAsync();
        UpdateStatus();
    }

    private void CancelIdleTimer()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    private void CancelMaxDurationTimer()
    {
        _maxDurationTimer?.Dispose();
        _maxDurationTimer = null;
    }

    private void UpdateStatus() => _notifier.SetState(State, _engine.IsLoaded, _engine.IsPinned);

    public async ValueTask DisposeAsync()
    {
        _audio.FramesAvailable -= OnFrames;
        _audio.Failed -= OnAudioFailed;
        _segmenter.SegmentCompleted -= OnSegmentCompleted;

        CancelIdleTimer();
        CancelMaxDurationTimer();
        _previewCancellation?.Dispose();
        _transition.Dispose();

        await _engine.DisposeAsync();
    }
}
