using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using Murmur.Core;
using Murmur.Core.Abstractions;
using Murmur.Platform;

namespace Murmur.App;

internal static class Program
{
    private const string MutexName = "Murmur.SingleInstance.9F2C1A7B";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Murmur is already running — look for the microphone icon in your tray.",
                "Murmur", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MurmurContext());
    }
}

/// <summary>
/// Composition root. Owns every component's lifetime and wires the tray menu to the
/// controller. Deliberately thin — all the sequencing lives in DictationController.
/// </summary>
internal sealed class MurmurContext : ApplicationContext
{
    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur");

    private readonly FileDiagnosticLog _log;
    private readonly SettingsStore _settingsStore;
    private readonly TrayShell _tray = new();
    private readonly OverlayWindow _overlay = new();
    private readonly WhisperEngine _engine;
    private readonly WhistleEngine _whistle;
    private readonly LanguageRoutedEngine _router;
    private readonly WasapiAudioCapture _audio;
    private readonly LowLevelHotkeySource _hotkeys;
    private readonly Win32TextInjector _injector;
    private readonly RunKeyStartupRegistrar _startup = new();
    private readonly System.Windows.Forms.Timer _startupTimer = new() { Interval = 1 };

    // Both need the VAD model on disk, so they are built after the download completes.
    private SileroVoiceSegmenter? _segmenter;
    private DictationController? _controller;

    private Settings _settings;
    private bool _modelsReady;

    public MurmurContext()
    {
        _log = new FileDiagnosticLog(Path.Combine(_dataDirectory, "murmur.log"));
        _settingsStore = new SettingsStore(Path.Combine(_dataDirectory, "settings.json"));
        _settings = _settingsStore.Load();

        _audio = new WasapiAudioCapture(_log);
        _hotkeys = new LowLevelHotkeySource(_log);
        _injector = new Win32TextInjector(_log);

        _log.WriteSessionHeader(
        [
            ("version", typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"),
            ("executable", Application.ExecutablePath),
            ("data directory", _dataDirectory),
            ("hotkey", _settings.Hotkey),
            ("language", _settings.Language),
            ("speech model", SpeechLanguages.ModelFor(_settings.Language).ToString()),
            ("insert mode", _settings.InsertMode),
            ("device", _settings.Device),
            ("hold threshold", $"{_settings.HoldThresholdMs} ms"),
            ("vad silence", $"{_settings.VadSilenceMs} ms"),
            ("idle unload", $"{_settings.IdleUnloadMinutes} min"),
            ("start with windows", _settings.StartWithWindows.ToString()),
        ]);

        _audio.LogDeviceInventory();

        var store = new ModelStore(Path.Combine(_dataDirectory, "models"));
        _engine = new WhisperEngine(store, _settings.Device, _log);
        _whistle = new WhistleEngine(store, _log);
        _router = new LanguageRoutedEngine(_engine, _whistle, () => _settings.Language, _log);

        WireTray();
        WireHotkeys();

        _tray.ApplySettings(_settings);
        _tray.SetState(DictationState.Idle, modelLoaded: false, modelPinned: false);

        // Startup work is deferred to a one-shot timer rather than run here, for two
        // reasons: blocking the constructor on an 870 MB download would freeze the app
        // before the message loop ever starts (so no balloon could render), and WinForms
        // does not install its SynchronizationContext until Application.Run — awaiting in
        // the constructor would resume continuations on the thread pool.
        _startupTimer.Tick += OnStartupTick;
        _startupTimer.Start();
    }

    private async void OnStartupTick(object? sender, EventArgs e)
    {
        _startupTimer.Stop();
        _startupTimer.Tick -= OnStartupTick;

        try
        {
            if (_engine.NeedsWhisperModelDownload)
            {
                _tray.Notify("Murmur", "Preparing the speech model. First run downloads about 870 MB.");
            }

            // Progress fires per 80 KB chunk. Throttle to the tooltip every ~10 MB —
            // a balloon per chunk would fire thousands of times.
            var lastReportedStep = -1L;
            var progress = new Progress<long>(bytes =>
            {
                var step = bytes / 10_000_000;
                if (step == lastReportedStep) return;
                lastReportedStep = step;
                _tray.SetTooltip($"Murmur — downloading model, {bytes / 1_000_000} MB");
            });

            await _engine.EnsureModelsDownloadedAsync(progress, CancellationToken.None);

            _segmenter = new SileroVoiceSegmenter(_engine.VadModelPath, _settings.VadSilenceMs);
            _controller = new DictationController(
                _audio, _segmenter, _router, _injector, _overlay, _tray, () => _settings,
                TimeProvider.System, _log);

            _modelsReady = true;
            _log.Write("startup", $"models ready — whisper and VAD loaded from {Path.Combine(_dataDirectory, "models")}");

            // Read the language now, not before the download: it may have been changed from
            // the menu meanwhile, and the switch handler leaves that case to startup.
            if (_router.ActiveModel == SpeechModel.Whistle && !await TryPrepareWhistleAsync()) return;

            _tray.SetState(DictationState.Idle, _router.IsLoaded, _router.IsPinned);
            _tray.Notify("Murmur", $"Ready. Press {_settings.Hotkey} to dictate.");
        }
        catch (Exception ex)
        {
            _log.Write("error", $"startup failed: {ex}");
            _tray.Notify("Murmur", $"Model download failed: {ex.Message}. Restart Murmur to retry.");
        }
    }

    /// <summary>
    /// Releases the model being left straight away rather than at the next dictation, and
    /// fetches Whistle the first time one of its languages is chosen.
    /// </summary>
    private async Task SwitchModelAsync()
    {
        // Startup prepares whichever model the language needs once it gets that far.
        if (!_modelsReady || _controller is not { } controller) return;

        if (_router.ActiveModel == SpeechModel.Whistle)
        {
            var downloading = _whistle.NeedsDownload;
            if (!await TryPrepareWhistleAsync()) return;
            if (downloading) _tray.Notify("Murmur", $"Whistle is ready. Press {_settings.Hotkey} to dictate.");
        }

        try
        {
            await _router.SwitchToCurrentLanguageAsync();
        }
        catch (Exception ex)
        {
            _log.Write("error", $"model switch failed: {ex}");
            _tray.Notify("Murmur", $"Could not switch models: {ex.Message}");
        }

        _tray.SetState(controller.State, _router.IsLoaded, _router.IsPinned);
    }

    /// <summary>
    /// Downloads Whistle if it is not cached. A failure is reported but not fatal: Whisper
    /// is unaffected, and the next Whistle dictation retries the download by itself.
    /// </summary>
    private async Task<bool> TryPrepareWhistleAsync()
    {
        if (!_whistle.NeedsDownload) return true;

        try
        {
            _tray.Notify("Murmur",
                $"Downloading the Whistle model (about {WhistleEngine.DownloadMegabytes} MB). This happens once.");

            await _whistle.EnsureDownloadedAsync(progress: null, CancellationToken.None);

            _log.Write("startup", "whistle engine and model downloaded");
            return true;
        }
        catch (Exception ex)
        {
            _log.Write("error", $"whistle download failed: {ex}");
            _tray.Notify("Murmur", $"The Whistle download failed: {ex.Message}. It will retry when you dictate.");
            return false;
        }
    }

    private void WireTray()
    {
        _tray.LanguageChanged += code =>
        {
            UpdateSettings(_settings with { Language = code });
            _ = SwitchModelAsync();
        };

        _tray.InsertModeChanged += mode => UpdateSettings(_settings with { InsertMode = mode });

        _tray.HotkeyChangeRequested += () =>
        {
            using var dialog = new HotkeyCaptureDialog(_settings.Hotkey);
            if (dialog.ShowDialog() != DialogResult.OK || dialog.SelectedHotkey is null) return;

            UpdateSettings(_settings with { Hotkey = dialog.SelectedHotkey });
            _hotkeys.Rebind(_settings.Hotkey);
        };

        _tray.StartWithWindowsToggled += enabled =>
        {
            if (enabled) _startup.Enable(Application.ExecutablePath);
            else _startup.Disable();

            UpdateSettings(_settings with { StartWithWindows = enabled });
        };

        // The controller does not exist until the model download finishes, so these are
        // wired once here and null-guarded rather than subscribed later.
        _tray.PreloadRequested += () => { if (_controller is { } c) _ = c.PreloadAsync(); };
        _tray.ReleaseGpuRequested += () => { if (_controller is { } c) _ = c.ReleaseGpuAsync(); };

        _tray.OpenLogRequested += () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_log.Path}\"")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                _tray.Notify("Murmur", $"Could not open the log folder: {ex.Message}");
            }
        };

        _tray.QuitRequested += () =>
        {
            _log.Write("startup", "quit requested from the tray menu");
            ExitThread();
        };
    }

    private void WireHotkeys()
    {
        _hotkeys.KeyEvent += (transition, timestamp) =>
        {
            // Runs on the hook thread. TrayShell marshals to the UI thread internally.
            if (!_modelsReady || _controller is not { } controller)
            {
                if (transition == KeyTransition.Down)
                {
                    _tray.Notify("Murmur", "Still downloading the speech model — one moment.");
                }

                return;
            }

            _ = controller.HandleKeyEventAsync(transition, timestamp);
        };

        _hotkeys.HookFailed += reason =>
            _tray.Notify("Murmur", $"The hotkey could not be registered: {reason}");

        _hotkeys.Rebind(_settings.Hotkey);
        _hotkeys.Start();
    }

    private void UpdateSettings(Settings updated)
    {
        _settings = updated;
        _settingsStore.Save(updated);
        _tray.ApplySettings(updated);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _startupTimer.Dispose();
            _hotkeys.Dispose();
            _controller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _segmenter?.Dispose();
            _audio.Dispose();
            _overlay.Dispose();
            _tray.Dispose();
        }

        base.Dispose(disposing);
    }
}
