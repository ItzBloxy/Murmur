using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Murmur.Core;
using Murmur.Core.Abstractions;

namespace Murmur.Platform;

/// <summary>
/// A <c>WH_KEYBOARD_LL</c> hook on a dedicated thread with its own message pump.
///
/// Windows silently removes hooks whose callback exceeds LowLevelHooksTimeout (~300ms), so
/// the callback only reads the event, decides swallow-or-pass, and raises the event. It
/// never transcribes, allocates large buffers or blocks. A watchdog reinstalls the hook if
/// Windows drops it anyway.
/// </summary>
public sealed partial class LowLevelHotkeySource : IHotkeySource
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;
    private const int HC_ACTION = 0;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly LowLevelKeyboardProc _callback;

    private Thread? _thread;
    private nint _hookHandle;
    private uint _threadId;
    private volatile bool _running;
    private volatile bool _keyIsDown;
    private Keys _boundKey = HotkeySwallowRule.Default;

    public LowLevelHotkeySource() => _callback = HookCallback;

    public event Action<KeyTransition, long>? KeyEvent;

    public void Rebind(string hotkey)
    {
        _boundKey = HotkeySwallowRule.Parse(hotkey);
        _keyIsDown = false;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        _thread = new Thread(PumpMessages)
        {
            IsBackground = true,
            Name = "Murmur hotkey hook",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void PumpMessages()
    {
        _threadId = GetCurrentThreadId();
        Install();

        // Reinstall the hook if Windows drops it for exceeding the callback timeout.
        using var watchdog = new System.Threading.Timer(
            _ => { if (_running && _hookHandle == 0) Install(); },
            null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        while (_running && GetMessage(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        Uninstall();
    }

    private void Install()
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(module.ModuleName), 0);
    }

    private void Uninstall()
    {
        if (_hookHandle == 0) return;
        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = 0;
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code != HC_ACTION) return CallNextHookEx(_hookHandle, code, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        var key = (Keys)data.vkCode;

        if (key != _boundKey) return CallNextHookEx(_hookHandle, code, wParam, lParam);

        var message = (int)wParam;
        var timestamp = _clock.ElapsedMilliseconds;

        if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            var transition = _keyIsDown ? KeyTransition.Repeat : KeyTransition.Down;
            _keyIsDown = true;
            KeyEvent?.Invoke(transition, timestamp);
        }
        else if (message is WM_KEYUP or WM_SYSKEYUP)
        {
            _keyIsDown = false;
            KeyEvent?.Invoke(KeyTransition.Up, timestamp);
        }

        return HotkeySwallowRule.ShouldSwallow(_boundKey)
            ? 1
            : CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    public void Dispose()
    {
        _running = false;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string lpModuleName);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint idThread, uint msg, nuint wParam, nint lParam);
}
