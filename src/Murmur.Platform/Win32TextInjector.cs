using System.Runtime.InteropServices;
using System.Windows.Forms;
using Murmur.Core.Abstractions;

namespace Murmur.Platform;

/// <summary>
/// Inserts transcribed text into the focused application.
///
/// Two safety properties matter here. First, the foreground window is verified against the
/// handle captured at record start — typing into whatever happened to grab focus is the one
/// unrecoverable mistake this app could make. Second, held modifiers are released before
/// pasting: hold-to-talk on the default Right Ctrl binding keeps that key down until
/// insertion begins, and a Ctrl+V sent on top of it lands corrupted.
/// </summary>
public sealed partial class Win32TextInjector : ITextInjector
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const int ClipboardRestoreDelayMs = 300;

    private static readonly ushort[] ModifierVirtualKeys =
    [
        0xA0, 0xA1,   // LSHIFT, RSHIFT
        0xA2, 0xA3,   // LCONTROL, RCONTROL
        0xA4, 0xA5,   // LMENU, RMENU
        0x5B, 0x5C,   // LWIN, RWIN
    ];

    public nint GetForegroundWindow() => GetForegroundWindowNative();

    /// <summary>
    /// Runs the injection on a short-lived STA thread. <see cref="Clipboard"/> throws
    /// <see cref="ThreadStateException"/> off an STA thread, and this is reached from the
    /// keyboard hook's thread, not the UI thread. A thread per insertion is free at human
    /// dictation rates and avoids threading a marshaller through the interface.
    /// </summary>
    public InjectionResult Inject(string text, nint expectedWindow)
    {
        var result = InjectionResult.Failed;

        var thread = new Thread(() => result = InjectCore(text, expectedWindow))
        {
            IsBackground = true,
            Name = "Murmur text injection",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    private static InjectionResult InjectCore(string text, nint expectedWindow)
    {
        if (GetForegroundWindowNative() != expectedWindow)
        {
            TrySetClipboardText(text);
            return InjectionResult.CopiedToClipboardFocusChanged;
        }

        ReleaseHeldModifiers();

        // A clipboard holding an image or files cannot be faithfully restored, so type
        // instead rather than silently destroying it.
        var clipboardIsRestorable = ClipboardHoldsOnlyText(out var savedText);

        if (clipboardIsRestorable && TryPaste(text, savedText)) return InjectionResult.Injected;

        return TypeUnicode(text) ? InjectionResult.Injected : InjectionResult.Failed;
    }

    private static bool TryPaste(string text, string? savedText)
    {
        if (!TrySetClipboardText(text)) return false;

        SendKeyChord(0x11 /* VK_CONTROL */, 0x56 /* V */);

        Thread.Sleep(ClipboardRestoreDelayMs);
        if (savedText is not null) TrySetClipboardText(savedText);
        else TryClearClipboard();

        return true;
    }

    private static bool ClipboardHoldsOnlyText(out string? savedText)
    {
        savedText = null;

        try
        {
            if (Clipboard.ContainsImage() || Clipboard.ContainsFileDropList() || Clipboard.ContainsAudio())
            {
                return false;
            }

            if (Clipboard.ContainsText()) savedText = Clipboard.GetText();
            return true;
        }
        catch (ExternalException)
        {
            return false;   // another application holds the clipboard open
        }
    }

    private static bool TrySetClipboardText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(30);
            }
        }

        return false;
    }

    private static void TryClearClipboard()
    {
        try { Clipboard.Clear(); } catch (ExternalException) { }
    }

    /// <summary>
    /// Synthesizes key-ups for any modifier the user is physically holding. Not restored
    /// afterwards — the user is about to release them anyway, and a stuck synthetic modifier
    /// is far worse than a missed one.
    /// </summary>
    private static void ReleaseHeldModifiers()
    {
        var inputs = new List<INPUT>();

        foreach (var vk in ModifierVirtualKeys)
        {
            if ((GetAsyncKeyState(vk) & 0x8000) == 0) continue;
            inputs.Add(KeyInput(vk, KEYEVENTF_KEYUP));
        }

        if (inputs.Count == 0) return;

        var array = inputs.ToArray();
        SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        Thread.Sleep(10);
    }

    private static void SendKeyChord(ushort modifier, ushort key)
    {
        INPUT[] inputs =
        [
            KeyInput(modifier, 0),
            KeyInput(key, 0),
            KeyInput(key, KEYEVENTF_KEYUP),
            KeyInput(modifier, KEYEVENTF_KEYUP),
        ];

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static bool TypeUnicode(string text)
    {
        foreach (var c in text)
        {
            INPUT[] inputs =
            [
                UnicodeInput(c, KEYEVENTF_UNICODE),
                UnicodeInput(c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP),
            ];

            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == 0) return false;
            Thread.Sleep(1);
        }

        return true;
    }

    private static INPUT KeyInput(ushort virtualKey, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        union = new INPUTUNION { ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = flags } },
    };

    private static INPUT UnicodeInput(char character, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        union = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = character, dwFlags = flags } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial nint GetForegroundWindowNative();

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
