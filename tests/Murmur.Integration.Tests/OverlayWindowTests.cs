using System.Windows.Forms;
using Murmur.App;
using Murmur.Core.Abstractions;

namespace Murmur.Integration.Tests;

/// <summary>
/// The overlay is driven entirely from off the UI thread — the hotkey pump raises events on
/// a pooled worker — so every one of these calls arrives cross-thread in production.
///
/// It silently failed to appear at all because a Form has no handle until it is first
/// shown, and Control.InvokeRequired returns false when there is no handle. The marshalling
/// check passed through, Show() ran on the worker, and the window was created on a thread
/// with no message loop, where it could never paint.
/// </summary>
[Trait("Category", "UI")]
public sealed class OverlayWindowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Showing_from_a_background_thread_makes_the_overlay_visible()
    {
        using var host = new OverlayHost();

        // Exactly what DictationController does, from exactly the kind of thread it uses.
        await Task.Run(() => host.Overlay.Show(), Ct);

        Assert.True(host.WaitFor(() => host.Form.Visible, TimeSpan.FromSeconds(5)),
            "overlay never became visible");
    }

    [Fact]
    public async Task Preview_text_set_from_a_background_thread_reaches_the_label()
    {
        using var host = new OverlayHost();
        await Task.Run(() => host.Overlay.Show(), Ct);

        await Task.Run(() => host.Overlay.SetPreview("hello from the pump"), Ct);

        Assert.True(host.WaitFor(() => host.LabelText.Contains("hello from the pump", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5)), $"label showed \"{host.LabelText}\" instead");
    }

    [Fact]
    public async Task Hiding_from_a_background_thread_hides_the_overlay()
    {
        using var host = new OverlayHost();
        await Task.Run(() => host.Overlay.Show(), Ct);
        Assert.True(host.WaitFor(() => host.Form.Visible, TimeSpan.FromSeconds(5)));

        await Task.Run(() => host.Overlay.Hide(), Ct);

        Assert.True(host.WaitFor(() => !host.Form.Visible, TimeSpan.FromSeconds(5)),
            "overlay never hid");
    }

    /// <summary>Runs an OverlayWindow on its own UI thread, the way the app does.</summary>
    private sealed class OverlayHost : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly Thread _thread;
        private OverlayWindow _overlay = null!;

        public OverlayHost()
        {
            _thread = new Thread(Run) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("overlay host never started");
        }

        public IOverlay Overlay => _overlay;

        public Form Form => _overlay;

        public string LabelText =>
            (string)_overlay.Invoke(() => _overlay.Controls.OfType<Label>().FirstOrDefault()?.Text ?? "");

        private void Run()
        {
            _overlay = new OverlayWindow();

            // A hidden pump form keeps the message loop alive without showing the overlay,
            // which must only become visible when Show() is called.
            using var pump = new Form { Opacity = 0, ShowInTaskbar = false, WindowState = FormWindowState.Minimized };
            pump.Shown += (_, _) => _ready.Set();

            Application.Run(pump);
        }

        public bool WaitFor(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if ((bool)_overlay.Invoke(condition)) return true;
                Thread.Sleep(75);
            }

            return false;
        }

        public void Dispose()
        {
            try
            {
                _overlay.Invoke(() =>
                {
                    _overlay.Dispose();
                    Application.ExitThread();
                });
            }
            catch (Exception)
            {
                // Already gone.
            }

            _thread.Join(TimeSpan.FromSeconds(3));
            _ready.Dispose();
        }
    }
}
