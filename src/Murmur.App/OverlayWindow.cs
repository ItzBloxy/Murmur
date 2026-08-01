using System.Drawing;
using System.Windows.Forms;
using Murmur.Core.Abstractions;

namespace Murmur.App;

/// <summary>
/// The live preview pill. Must never take focus — if it did, the caret would leave the
/// user's textbox and the whole insertion would land in the wrong place.
///
/// <c>WS_EX_NOACTIVATE</c> keeps focus, <c>WS_EX_TRANSPARENT</c> lets clicks through and
/// <c>WS_EX_TOOLWINDOW</c> keeps it out of alt-tab.
/// </summary>
public sealed partial class OverlayWindow : Form, IOverlay
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;

    private readonly Label _label;

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(24, 24, 27);
        Opacity = 0.92;
        Size = new Size(560, 84);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(16, 8, 16, 8),
        };

        Controls.Add(_label);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WM_MOUSEACTIVATE)
        {
            message.Result = MA_NOACTIVATE;
            return;
        }

        base.WndProc(ref message);
    }

    void IOverlay.Show() => RunOnUiThread(() =>
    {
        PositionOnActiveScreen();
        _label.Text = "🎙  Listening…";
        base.Show();
    });

    void IOverlay.SetPreview(string text) => RunOnUiThread(() =>
        _label.Text = string.IsNullOrWhiteSpace(text) ? "🎙  Listening…" : LastTwoLines(text));

    void IOverlay.SetTranscribing() => RunOnUiThread(() => _label.Text = "⏳  Transcribing…");

    void IOverlay.Hide() => RunOnUiThread(base.Hide);

    /// <summary>
    /// Bottom-center of the monitor holding the focused window — not the monitor holding
    /// the cursor, which is frequently a different one. Safe to read here because the
    /// overlay never activates, so the target window is still foreground.
    /// </summary>
    private void PositionOnActiveScreen()
    {
        var foreground = GetForegroundWindow();
        var screen = foreground != 0 ? Screen.FromHandle(foreground) : Screen.PrimaryScreen!;
        var area = screen.WorkingArea;

        Location = new Point(
            area.Left + (area.Width - Width) / 2,
            area.Bottom - Height - 80);
    }

    /// <summary>Keeps the tail of a long transcript visible rather than the beginning.</summary>
    private static string LastTwoLines(string text)
    {
        const int charactersPerLine = 62;
        if (text.Length <= charactersPerLine * 2) return text;
        return "… " + text[^(charactersPerLine * 2)..];
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
}
