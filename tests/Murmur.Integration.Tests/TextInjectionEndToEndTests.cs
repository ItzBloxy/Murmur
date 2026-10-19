using System.Runtime.InteropServices;
using System.Windows.Forms;
using Murmur.Core.Abstractions;
using Murmur.Platform;

namespace Murmur.Integration.Tests;

/// <summary>
/// Drives the real injector against a real focused window and reads the text back.
///
/// This is the only test that proves dictation actually reaches an application. Everything
/// upstream of it worked for a long time while insertion silently failed on a mis-sized
/// INPUT struct, and no amount of unit testing could have caught that.
///
/// The target window belongs to this test rather than to Notepad: driving someone else's
/// editor means competing for focus with whatever the user has open, and killing stray
/// instances afterwards risks discarding their unsaved work.
/// </summary>
[Trait("Category", "UI")]
public sealed partial class TextInjectionEndToEndTests
{
    [Fact]
    public void Text_is_actually_inserted_into_the_focused_window()
    {
        using var host = new TargetWindow();

        Assert.SkipUnless(host.WaitUntilForeground(TimeSpan.FromSeconds(5)),
            "Could not take the foreground; another window is holding it.");

        const string expected = "Murmur injection test 123";

        var result = new Win32TextInjector().Inject(expected, host.Handle);

        Assert.Equal(InjectionResult.Injected, result);
        Assert.Equal(expected, host.WaitForText(expected, TimeSpan.FromSeconds(5)));
    }

    /// <summary>A borrowed, focusable text field running its own message loop.</summary>
    private sealed partial class TargetWindow : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly Thread _thread;

        private Form _form = null!;
        private TextBox _textBox = null!;

        public TargetWindow()
        {
            _thread = new Thread(RunMessageLoop) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            _ready.Wait(TimeSpan.FromSeconds(10));
        }

        public nint Handle { get; private set; }

        private void RunMessageLoop()
        {
            _textBox = new TextBox { Dock = DockStyle.Fill, Multiline = true };
            _form = new Form
            {
                Text = "Murmur injection target",
                Width = 500,
                Height = 200,
                TopMost = true,
                StartPosition = FormStartPosition.CenterScreen,
            };

            _form.Controls.Add(_textBox);
            _form.Shown += (_, _) =>
            {
                Handle = _form.Handle;
                _form.Activate();
                _textBox.Focus();
                SetForegroundWindow(_form.Handle);
                _ready.Set();
            };

            Application.Run(_form);
        }

        public bool WaitUntilForeground(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Handle != 0 && GetForegroundWindow() == Handle) return true;

                if (Handle != 0)
                {
                    _form.BeginInvoke(() =>
                    {
                        _form.Activate();
                        SetForegroundWindow(_form.Handle);
                        _textBox.Focus();
                    });
                }

                Thread.Sleep(100);
            }

            return false;
        }

        /// <summary>Typing is asynchronous, so poll rather than assuming it has landed.</summary>
        public string WaitForText(string expected, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            var text = "";

            while (DateTime.UtcNow < deadline)
            {
                text = (string)_form.Invoke(() => _textBox.Text);
                if (text.Contains(expected, StringComparison.Ordinal)) return expected;
                Thread.Sleep(100);
            }

            return text;
        }

        public void Dispose()
        {
            try
            {
                if (_form is { IsDisposed: false }) _form.Invoke(_form.Close);
            }
            catch (Exception)
            {
                // Already torn down.
            }

            _thread.Join(TimeSpan.FromSeconds(3));
            _ready.Dispose();
        }

        [LibraryImport("user32.dll")]
        private static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetForegroundWindow(nint hWnd);
    }
}
