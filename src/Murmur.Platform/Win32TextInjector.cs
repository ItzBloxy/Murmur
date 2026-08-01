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
public sealed partial class Win32TextInjector(IDiagnosticLog? log = null) : ITextInjector
{
    private readonly IDiagnosticLog _log = log ?? NullDiagnosticLog.Instance;

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

    private InjectionResult InjectCore(string text, nint expectedWindow)
    {
        var actual = GetForegroundWindowNative();
        _log.Write("inject", $"expected {WindowInfo.Describe(expectedWindow)}");
        _log.Write("inject", $"actual   {WindowInfo.Describe(actual)}");

        if (actual != expectedWindow)
        {
            _log.Write("inject", "focus moved during dictation — copying to clipboard instead of typing");
            TrySetClipboardText(text);
            return InjectionResult.CopiedToClipboardFocusChanged;
        }

        var held = DescribeHeldModifiers();
        if (held.Length > 0) _log.Write("inject", $"releasing held modifiers: {held}");
        ReleaseHeldModifiers();

        // A clipboard holding an image or files cannot be faithfully restored, so type
        // instead rather than silently destroying it.
        var clipboardIsRestorable = ClipboardHoldsOnlyText(out var savedText);
        _log.Write("inject", $"clipboard restorable: {clipboardIsRestorable}");

        if (clipboardIsRestorable && TryPaste(text, savedText))
        {
            _log.Write("inject", "inserted via clipboard paste");
            return InjectionResult.Injected;
        }

        _log.Write("inject", "falling back to synthetic typing");
        if (TypeUnicode(text))
        {
            _log.Write("inject", "inserted via synthetic typing");
            return InjectionResult.Injected;
        }

        _log.Write("inject", "SendInput reported failure — nothing was inserted");
        return InjectionResult.Failed;
    }

    private static string DescribeHeldModifiers()
    {
        var names = new List<string>();
        string[] labels = ["LShift", "RShift", "LCtrl", "RCtrl", "LAlt", "RAlt", "LWin", "RWin"];

        for (var i = 0; i < ModifierVirtualKeys.Length; i++)
        {
            if ((GetAsyncKeyState(ModifierVirtualKeys[i]) & 0x8000) != 0) names.Add(labels[i]);
        }

        return string.Join(", ", names);
    }

    private bool TryPaste(string text, string? savedText)
    {
        if (!TrySetClipboardText(text))
        {
            _log.Write("inject", "could not place the transcript on the clipboard");
            return false;
        }

        if (!SendKeyChord(0x11 /* VK_CONTROL */, 0x56 /* V */))
        {
            _log.Write("inject",
                $"SendInput rejected Ctrl+V (Win32 error {Marshal.GetLastWin32Error()}, " +
                $"cbSize={Marshal.SizeOf<INPUT>()})");

            if (savedText is not null) TrySetClipboardText(savedText);
            return false;
        }

        Thread.Sleep(ClipboardRestoreDelayMs);
        if (savedText is not null) TrySetClipboardText(savedText);
        else TryClearClipboard();

        return true;
    }

    private bool ClipboardHoldsOnlyText(out string? savedText)
    {
        savedText = null;

        try
        {
            if (Clipboard.ContainsImage()) { _log.Write("inject", "clipboard holds an image — will not clobber it"); return false; }
            if (Clipboard.ContainsFileDropList()) { _log.Write("inject", "clipboard holds files — will not clobber it"); return false; }
            if (Clipboard.ContainsAudio()) { _log.Write("inject", "clipboard holds audio — will not clobber it"); return false; }

            if (Clipboard.ContainsText()) savedText = Clipboard.GetText();
            return true;
        }
        catch (ExternalException ex)
        {
            _log.Write("inject", $"clipboard is locked by another application: {ex.Message}");
            return false;
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

    private static bool SendKeyChord(ushort modifier, ushort key)
    {
        INPUT[] inputs =
        [
            KeyInput(modifier, 0),
            KeyInput(key, 0),
            KeyInput(key, KEYEVENTF_KEYUP),
            KeyInput(modifier, KEYEVENTF_KEYUP),
        ];

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    private bool TypeUnicode(string text)
    {
        foreach (var c in text)
        {
            INPUT[] inputs =
            [
                UnicodeInput(c, KEYEVENTF_UNICODE),
                UnicodeInput(c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP),
            ];

            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == 0)
            {
                _log.Write("inject",
                    $"SendInput rejected input (Win32 error {Marshal.GetLastWin32Error()}, " +
                    $"cbSize={Marshal.SizeOf<INPUT>()}) while typing '{c}'");
                return false;
            }

            Thread.Sleep(1);
        }

        return true;
    }

    // Every synthesized event carries the signature so our own hook discards it. Omitting it
    // makes the app retrigger its own hotkey the instant it inserts text.
    private static INPUT KeyInput(ushort virtualKey, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        union = new INPUTUNION
        {
            ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = flags, dwExtraInfo = SyntheticInput.Signature },
        },
    };

    private static INPUT UnicodeInput(char character, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        union = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = character,
                dwFlags = flags,
                dwExtraInfo = SyntheticInput.Signature,
            },
        },
    };

    /// <summary>
    /// Exposed so a test can assert this matches the OS ABI. It silently did not: the union
    /// originally declared only KEYBDINPUT, making INPUT 32 bytes on x64 where Windows
    /// requires 40. SendInput validates cbSize and returns 0 on a mismatch, so every
    /// insertion — paste and typing alike — failed without any visible cause.
    /// </summary>
    public static int NativeInputStructSize => Marshal.SizeOf<INPUT>();

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION union;
    }

    /// <summary>
    /// Must carry the largest member, not just the one we use. MOUSEINPUT is the largest,
    /// and its size is what sets sizeof(INPUT).
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial nint GetForegroundWindowNative();

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
