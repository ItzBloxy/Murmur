using Murmur.Core;
using Murmur.Core.Abstractions;
using Murmur.Platform;

namespace Murmur.Integration.Tests;

/// <summary>
/// Installs the real keyboard hook and then makes the real injector type, to prove the app
/// does not trigger its own hotkey.
///
/// Text insertion synthesizes Ctrl for the paste chord and key-ups for any held modifier.
/// The hook sees all keyboard input, including ours, so without a marker it treats those as
/// genuine presses. Its held-key state then disagrees with the physical keyboard and the
/// next auto-repeat reads as a fresh key-down — which restarted dictation immediately after
/// every insertion.
/// </summary>
[Trait("Category", "UI")]
public sealed class SelfTriggerTests
{
    [Fact]
    public void Injected_keystrokes_do_not_fire_the_hotkey()
    {
        var events = new List<KeyTransition>();

        using var hotkeys = new LowLevelHotkeySource();
        hotkeys.KeyEvent += (transition, _) => { lock (events) events.Add(transition); };

        // Bind generic Ctrl: the paste chord sends VK_CONTROL, so this is the binding most
        // exposed to self-triggering.
        hotkeys.Rebind(nameof(System.Windows.Forms.Keys.ControlKey));
        hotkeys.Start();

        // Give the hook thread time to install before generating input.
        Thread.Sleep(500);

        var injector = new Win32TextInjector();
        var foreground = injector.GetForegroundWindow();

        // Inject against the current foreground window. Whether the text lands is irrelevant
        // here — what matters is that the synthetic Ctrl never reaches our hotkey handler.
        injector.Inject("murmur self trigger check", foreground);

        Thread.Sleep(750);   // let anything the hook posted drain through the pump

        lock (events)
        {
            Assert.True(events.Count == 0,
                $"the injector triggered our own hotkey {events.Count} time(s): " +
                string.Join(", ", events));
        }
    }
}
