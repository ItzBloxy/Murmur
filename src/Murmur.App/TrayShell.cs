using System.Windows.Forms;
using Murmur.Core;
using Murmur.Core.Abstractions;

namespace Murmur.App;

/// <summary>
/// The tray icon, its context menu and the balloon notifier. The status label carries two
/// independent facts — the dictation state and whether the model is resident — because
/// model residency is orthogonal to what the app is doing.
/// </summary>
public sealed class TrayShell : INotifier, IDisposable
{
    /// <summary>
    /// Hidden control used purely to marshal onto the UI thread. The keyboard hook runs on
    /// its own thread, so every call that reaches the tray from the controller arrives
    /// off-thread — and NotifyIcon, like every WinForms component, is not thread-safe.
    /// </summary>
    private readonly Control _marshaller = new();

    private readonly NotifyIcon _icon = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _danish = new("Danish");
    private readonly ToolStripMenuItem _english = new("English");
    private readonly ToolStripMenuItem _insertOnStop = new("When I stop  (best accuracy)");
    private readonly ToolStripMenuItem _insertAsYouSpeak = new("As I speak  (each phrase)");
    private readonly ToolStripMenuItem _hotkey = new();
    private readonly ToolStripMenuItem _startWithWindows = new("Start with Windows") { CheckOnClick = true };
    private readonly ToolStripMenuItem _preload = new("Preload model");
    private readonly ToolStripMenuItem _release = new("Release GPU now");
    private readonly ToolStripMenuItem _openLog = new("Open diagnostic log");

    public TrayShell()
    {
        // Forces handle creation on the UI thread so InvokeRequired reports correctly.
        _marshaller.CreateControl();

        var language = new ToolStripMenuItem("Language");
        language.DropDownItems.AddRange([_danish, _english]);

        var insertMode = new ToolStripMenuItem("Insert text");
        insertMode.DropDownItems.AddRange([_insertOnStop, _insertAsYouSpeak]);

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _status,
            new ToolStripSeparator(),
            language,
            insertMode,
            new ToolStripSeparator(),
            _hotkey,
            _startWithWindows,
            new ToolStripSeparator(),
            _preload,
            _release,
            new ToolStripSeparator(),
            _openLog,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Quit", null, (_, _) => QuitRequested?.Invoke()),
        ]);

        _danish.Click += (_, _) => LanguageChanged?.Invoke("da");
        _english.Click += (_, _) => LanguageChanged?.Invoke("en");
        _insertOnStop.Click += (_, _) => InsertModeChanged?.Invoke("onStop");
        _insertAsYouSpeak.Click += (_, _) => InsertModeChanged?.Invoke("asYouSpeak");
        _hotkey.Click += (_, _) => HotkeyChangeRequested?.Invoke();
        _startWithWindows.Click += (_, _) => StartWithWindowsToggled?.Invoke(_startWithWindows.Checked);
        _preload.Click += (_, _) => PreloadRequested?.Invoke();
        _release.Click += (_, _) => ReleaseGpuRequested?.Invoke();
        _openLog.Click += (_, _) => OpenLogRequested?.Invoke();

        _icon.Icon = TrayIcons.Idle;
        _icon.Text = "Murmur";
        _icon.ContextMenuStrip = menu;
        _icon.Visible = true;
    }

    public event Action<string>? LanguageChanged;
    public event Action<string>? InsertModeChanged;
    public event Action? HotkeyChangeRequested;
    public event Action<bool>? StartWithWindowsToggled;
    public event Action? PreloadRequested;
    public event Action? ReleaseGpuRequested;
    public event Action? OpenLogRequested;
    public event Action? QuitRequested;

    public void ApplySettings(Settings settings)
    {
        _danish.Checked = settings.Language == "da";
        _english.Checked = settings.Language == "en";
        _insertAsYouSpeak.Checked = settings.InsertMode == "asYouSpeak";
        _insertOnStop.Checked = !_insertAsYouSpeak.Checked;
        _hotkey.Text = $"Hotkey…  ({settings.Hotkey})";
        _startWithWindows.Checked = settings.StartWithWindows;
    }

    public void Notify(string title, string message) => RunOnUiThread(() =>
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(4000);
    });

    /// <summary>
    /// Updates the hover tooltip without raising a balloon. Used for download progress —
    /// a balloon per chunk would fire thousands of times.
    /// </summary>
    public void SetTooltip(string text) => RunOnUiThread(() => _icon.Text = Truncate(text));

    public void SetState(DictationState state, bool modelLoaded, bool modelPinned) => RunOnUiThread(() =>
    {
        _icon.Icon = state switch
        {
            DictationState.Recording => TrayIcons.Recording,
            DictationState.Transcribing => TrayIcons.Transcribing,
            _ => TrayIcons.Idle,
        };

        var suffix = (modelLoaded, modelPinned) switch
        {
            (true, true) => " · model pinned",
            (true, false) => " · model loaded",
            _ => "",
        };

        var label = $"Murmur — {state}{suffix}";
        _status.Text = label;
        _icon.Text = Truncate(label);

        _preload.Enabled = !modelLoaded;
        _release.Enabled = modelLoaded;
    });

    /// <summary>NotifyIcon.Text throws above 63 characters.</summary>
    private static string Truncate(string text) => text.Length > 63 ? text[..63] : text;

    private void RunOnUiThread(Action action)
    {
        if (_marshaller.IsDisposed) return;

        if (_marshaller.InvokeRequired) _marshaller.BeginInvoke(action);
        else action();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _marshaller.Dispose();
    }
}
