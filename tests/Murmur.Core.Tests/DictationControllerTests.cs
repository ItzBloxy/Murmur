using Microsoft.Extensions.Time.Testing;
using Murmur.Core;
using Murmur.Core.Abstractions;

namespace Murmur.Core.Tests;

public sealed class DictationControllerTests
{
    private readonly FakeAudioCapture _audio = new();
    private readonly FakeVoiceSegmenter _segmenter = new();
    private readonly FakeTranscriptionEngine _engine = new();
    private readonly FakeTextInjector _injector = new();
    private readonly FakeOverlay _overlay = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeTimeProvider _time = new();
    private Settings _settings = new();

    private DictationController Create() => new(
        _audio, _segmenter, _engine, _injector, _overlay, _notifier, () => _settings, _time);

    /// <summary>Tap on, tap off — the common case.</summary>
    private async Task<DictationController> RecordAndStopAsync(DictationController? existing = null)
    {
        var controller = existing ?? Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _segmenter.Push(new float[16000]);
        await controller.HandleKeyEventAsync(KeyTransition.Down, 2000);
        return controller;
    }

    [Fact]
    public async Task Tap_starts_recording_and_shows_the_overlay()
    {
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        Assert.Equal(DictationState.Recording, controller.State);
        Assert.True(_audio.IsCapturing);
        Assert.True(_overlay.IsVisible);
        Assert.Equal(1, _segmenter.ResetCount);
    }

    [Fact]
    public async Task Recording_captures_the_target_window_at_start()
    {
        _injector.ForegroundWindow = 42;
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _injector.ForegroundWindow = 99;   // focus moves after we started
        _segmenter.Push(new float[16000]);
        await controller.HandleKeyEventAsync(KeyTransition.Down, 2000);

        Assert.Equal((nint)42, _injector.Injections.Single().ExpectedWindow);
    }

    [Fact]
    public async Task Second_tap_transcribes_the_full_buffer_and_injects()
    {
        var controller = await RecordAndStopAsync();

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.False(_audio.IsCapturing);
        Assert.False(_overlay.IsVisible);
        Assert.Equal("final text", _injector.Injections.Single().Text);
        Assert.Contains(_engine.Calls, c => c.Quality == TranscriptionQuality.Final && c.SampleCount == 16000);
    }

    [Fact]
    public async Task Hold_records_until_release_then_injects()
    {
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        Assert.Equal(DictationState.Recording, controller.State);
        _segmenter.Push(new float[8000]);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 1000);   // past the 400ms threshold

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Equal("final text", _injector.Injections.Single().Text);
    }

    /// <summary>
    /// The load is dispatched to the thread pool rather than run inline — it is seconds of
    /// synchronous native work, and inline it would block whichever thread delivered the
    /// hotkey. So await the background work rather than assuming it already finished.
    /// </summary>
    [Fact]
    public async Task Model_loading_begins_while_recording_not_after()
    {
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        await controller.WaitForIdleAsync();

        Assert.True(_engine.IsLoaded);
        Assert.Equal(DictationState.Recording, controller.State);
    }

    [Fact]
    public async Task Completed_segments_stream_into_the_overlay_preview()
    {
        _engine.Results = _ => [new TranscribedSegment("hello there", 0.05f)];
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        await controller.HandleSegmentAsync(new float[4000]);

        Assert.Equal("hello there", _overlay.LastPreview);
        Assert.Contains(_engine.Calls, c => c.Quality == TranscriptionQuality.Preview);
    }

    [Fact]
    public async Task Preview_segments_accumulate_in_order()
    {
        var texts = new Queue<string>(["first", "second"]);
        _engine.Results = _ => [new TranscribedSegment(texts.Dequeue(), 0.05f)];
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        await controller.HandleSegmentAsync(new float[4000]);
        await controller.HandleSegmentAsync(new float[4000]);

        Assert.Equal("first second", _overlay.LastPreview);
    }

    [Fact]
    public async Task Hallucinated_previews_are_not_shown()
    {
        _engine.Results = _ => [new TranscribedSegment("Tak fordi du så med", 0.05f)];
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        await controller.HandleSegmentAsync(new float[4000]);

        Assert.Equal("", _overlay.LastPreview);
    }

    [Fact]
    public async Task Focus_change_routes_to_the_clipboard_with_a_notification()
    {
        _injector.NextResult = InjectionResult.CopiedToClipboardFocusChanged;

        await RecordAndStopAsync();

        Assert.Contains(_notifier.Messages, m => m.Contains("clipboard", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Empty_transcript_injects_nothing_and_notifies_nothing()
    {
        _engine.Results = _ => [];

        var controller = await RecordAndStopAsync();

        Assert.Empty(_injector.Injections);
        Assert.Empty(_notifier.Messages);
        Assert.Equal(DictationState.Idle, controller.State);
    }

    [Fact]
    public async Task Fully_hallucinated_transcript_injects_nothing()
    {
        _engine.Results = _ => [new TranscribedSegment("Thank you for watching", 0.05f)];

        await RecordAndStopAsync();

        Assert.Empty(_injector.Injections);
        Assert.Empty(_notifier.Messages);
    }

    [Fact]
    public async Task Empty_audio_buffer_skips_transcription_entirely()
    {
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        await controller.HandleKeyEventAsync(KeyTransition.Down, 2000);   // no audio pushed

        Assert.Empty(_injector.Injections);
        Assert.DoesNotContain(_engine.Calls, c => c.Quality == TranscriptionQuality.Final);
        Assert.Equal(DictationState.Idle, controller.State);
    }

    /// <summary>
    /// The gate that actually stops silence hallucinations. Whisper returns "Thank you."
    /// for four seconds of digital silence with NoSpeechProbability 0.0, so nothing
    /// downstream of the model can be trusted to catch it — it has to not run at all.
    /// </summary>
    [Fact]
    public async Task Recording_with_no_detected_speech_never_reaches_the_engine()
    {
        _segmenter.SpeechPresent = false;
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _segmenter.Push(new float[16000 * 4]);   // four seconds: a deliberate silent recording
        await controller.HandleKeyEventAsync(KeyTransition.Down, 5000);

        Assert.DoesNotContain(_engine.Calls, c => c.Quality == TranscriptionQuality.Final);
        Assert.Empty(_injector.Injections);
        Assert.Equal(DictationState.Idle, controller.State);

        // Deliberately silent for four seconds: say nothing rather than nag.
        Assert.Empty(_notifier.Messages);
    }

    /// <summary>
    /// A sub-second speechless recording is nearly always a mistimed gesture — the press
    /// was read as push-to-talk and ended before any speech was captured. Saying nothing
    /// leaves the user with no idea why their dictation vanished.
    /// </summary>
    [Fact]
    public async Task A_very_short_speechless_recording_explains_itself()
    {
        _segmenter.SpeechPresent = false;
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        _segmenter.Push(new float[4800]);   // 0.3s, exactly what a mistimed tap produces
        await controller.HandleKeyEventAsync(KeyTransition.Up, 800);

        Assert.Empty(_injector.Injections);
        Assert.Contains(_notifier.Messages, m => m.Contains("too short", StringComparison.OrdinalIgnoreCase));
    }

    // As-you-speak mode: each phrase is typed as it completes, and nothing is ever revised
    // or deleted. The cost is that Whisper never sees the whole utterance at once.

    [Fact]
    public async Task As_you_speak_mode_inserts_each_phrase_as_it_completes()
    {
        _settings = _settings with { InsertMode = "asYouSpeak" };
        _engine.Results = _ => [new TranscribedSegment("first phrase", 0.05f)];
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        await controller.HandleSegmentAsync(new float[4000]);

        Assert.Equal("first phrase", _injector.Injections.Single().Text);
    }

    [Fact]
    public async Task As_you_speak_mode_separates_successive_phrases_with_a_space()
    {
        _settings = _settings with { InsertMode = "asYouSpeak" };
        var texts = new Queue<string>(["first", "second"]);
        _engine.Results = _ => [new TranscribedSegment(texts.Dequeue(), 0.05f)];
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        await controller.HandleSegmentAsync(new float[4000]);
        await controller.HandleSegmentAsync(new float[4000]);

        Assert.Equal(["first", " second"], _injector.Injections.Select(i => i.Text));
    }

    /// <summary>
    /// The whole point of tracking the tail. Transcribing the full buffer at stop would
    /// insert the entire dictation a second time, after every phrase was already typed.
    /// </summary>
    [Fact]
    public async Task As_you_speak_mode_only_transcribes_the_undelivered_tail_at_stop()
    {
        _settings = _settings with { InsertMode = "asYouSpeak" };
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        _segmenter.Push(new float[16000 * 10]);          // ten seconds captured in total
        _segmenter.UnconsumedTail = new float[16000];    // one second not yet delivered

        await controller.HandleKeyEventAsync(KeyTransition.Down, 2000);

        var final = Assert.Single(_engine.Calls, c => c.Quality == TranscriptionQuality.Final);
        Assert.Equal(16000, final.SampleCount);
    }

    [Fact]
    public async Task On_stop_mode_does_not_insert_while_recording()
    {
        _settings = _settings with { InsertMode = "onStop" };
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        await controller.HandleSegmentAsync(new float[4000]);

        Assert.Empty(_injector.Injections);
    }

    [Fact]
    public async Task On_stop_mode_transcribes_the_whole_buffer()
    {
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        _segmenter.Push(new float[16000 * 3]);
        _segmenter.UnconsumedTail = new float[16000];   // ignored in this mode

        await controller.HandleKeyEventAsync(KeyTransition.Down, 2000);

        var final = Assert.Single(_engine.Calls, c => c.Quality == TranscriptionQuality.Final);
        Assert.Equal(16000 * 3, final.SampleCount);
    }

    [Fact]
    public async Task Microphone_failure_aborts_without_injecting()
    {
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _segmenter.Push(new float[16000]);

        _audio.EmitFailure("Device disconnected");
        await controller.WaitForIdleAsync();

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Empty(_injector.Injections);
        Assert.Contains(_notifier.Messages, m => m.Contains("Device disconnected"));
    }

    [Fact]
    public async Task Recording_can_restart_after_a_microphone_failure()
    {
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _audio.EmitFailure("Device disconnected");
        await controller.WaitForIdleAsync();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 5000);

        Assert.Equal(DictationState.Recording, controller.State);
    }

    [Fact]
    public async Task Audio_start_failure_notifies_and_stays_idle()
    {
        _audio.ThrowOnStart = new InvalidOperationException("No input device");
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Contains(_notifier.Messages, m => m.Contains("No input device"));
    }

    [Fact]
    public async Task Max_recording_duration_stops_and_finalizes()
    {
        _settings = _settings with { MaxRecordingMinutes = 1 };
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _segmenter.Push(new float[16000]);

        _time.Advance(TimeSpan.FromMinutes(1));
        await controller.WaitForIdleAsync();

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Equal("final text", _injector.Injections.Single().Text);
        Assert.Contains(_notifier.Messages, m => m.Contains("maximum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Idle_timer_unloads_the_model()
    {
        _settings = _settings with { IdleUnloadMinutes = 5 };
        await RecordAndStopAsync();
        Assert.True(_engine.IsLoaded);

        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Yield();

        Assert.False(_engine.IsLoaded);
        Assert.Equal(1, _engine.UnloadCount);
    }

    [Fact]
    public async Task Pinning_suspends_the_idle_unload()
    {
        var controller = Create();
        await controller.PreloadAsync();
        Assert.True(_engine.IsLoaded);
        Assert.True(_engine.IsPinned);

        _time.Advance(TimeSpan.FromHours(1));
        await Task.Yield();

        Assert.True(_engine.IsLoaded);
        Assert.Equal(0, _engine.UnloadCount);
    }

    [Fact]
    public async Task Dictating_does_not_clear_the_pin()
    {
        var controller = Create();
        await controller.PreloadAsync();

        await RecordAndStopAsync(controller);
        _time.Advance(TimeSpan.FromHours(1));
        await Task.Yield();

        Assert.True(_engine.IsPinned);
        Assert.True(_engine.IsLoaded);
    }

    [Fact]
    public async Task Release_gpu_unloads_immediately_and_clears_the_pin()
    {
        var controller = Create();
        await controller.PreloadAsync();

        await controller.ReleaseGpuAsync();

        Assert.False(_engine.IsLoaded);
        Assert.False(_engine.IsPinned);
    }

    [Fact]
    public async Task Model_load_failure_notifies_and_does_not_crash_the_recording()
    {
        _engine.ThrowOnLoad = new InvalidOperationException("CUDA out of memory");
        var controller = Create();

        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        await controller.WaitForIdleAsync();

        Assert.Equal(DictationState.Recording, controller.State);
        Assert.Contains(_notifier.Messages, m => m.Contains("CUDA out of memory"));
    }

    [Fact]
    public async Task Stopping_cancels_an_in_flight_preview()
    {
        var gate = new TaskCompletionSource();
        _engine.Gate = gate;
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        _segmenter.Push(new float[16000]);

        var preview = controller.HandleSegmentAsync(new float[4000]);
        var stop = controller.HandleKeyEventAsync(KeyTransition.Down, 2000);

        await preview;                 // must complete without throwing
        _engine.Gate = null;
        gate.SetResult();
        await stop;

        Assert.Equal(DictationState.Idle, controller.State);
    }

    [Fact]
    public async Task Language_setting_is_passed_to_the_engine()
    {
        _settings = _settings with { Language = "en" };

        await RecordAndStopAsync();

        Assert.Equal("en", _engine.LastLanguage);
    }

    [Fact]
    public async Task Status_reports_recording_then_idle()
    {
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);
        Assert.Equal(DictationState.Recording, _notifier.LastState);

        _segmenter.Push(new float[16000]);
        await controller.HandleKeyEventAsync(KeyTransition.Down, 2000);

        Assert.Equal(DictationState.Idle, _notifier.LastState);
        Assert.True(_notifier.LastModelLoaded);
    }

    [Fact]
    public async Task Audio_frames_flow_into_the_segmenter()
    {
        var controller = Create();
        await controller.HandleKeyEventAsync(KeyTransition.Down, 0);
        await controller.HandleKeyEventAsync(KeyTransition.Up, 50);

        _audio.EmitFrames(new float[1600]);

        Assert.Equal(1600, _segmenter.FullBuffer.Length);
    }
}
