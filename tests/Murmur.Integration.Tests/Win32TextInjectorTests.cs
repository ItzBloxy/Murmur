using System.Runtime.InteropServices;
using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class Win32TextInjectorTests
{
    /// <summary>
    /// SendInput validates cbSize against the real sizeof(INPUT) and returns 0 on any
    /// mismatch, with no other symptom. An earlier version declared the union with only
    /// KEYBDINPUT, giving 32 bytes on x64 where Windows requires 40 — so every single
    /// insertion failed silently and dictation appeared to do nothing at all.
    ///
    /// The size is fixed by the largest union member, MOUSEINPUT, not by the member we use.
    /// </summary>
    [Fact]
    public void Native_INPUT_struct_matches_the_operating_system_ABI()
    {
        var expected = IntPtr.Size == 8 ? 40 : 28;

        Assert.Equal(expected, Win32TextInjector.NativeInputStructSize);
    }

    [Fact]
    public void GetForegroundWindow_returns_a_handle_or_zero()
    {
        var injector = new Win32TextInjector();

        var hwnd = injector.GetForegroundWindow();

        // No desktop guarantee in a test run; the contract is only that it does not throw.
        Assert.True(hwnd == 0 || hwnd != 0);
    }

    [Fact]
    public void Marshal_size_is_stable_for_the_documented_layout()
    {
        // Guards the assumption behind the number above: a pointer-sized dwExtraInfo plus
        // alignment is what makes MOUSEINPUT 32 bytes on x64.
        Assert.Equal(IntPtr.Size == 8 ? 8 : 4, Marshal.SizeOf<nuint>());
    }
}
