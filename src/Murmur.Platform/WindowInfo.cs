using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Murmur.Platform;

/// <summary>
/// Describes a window handle for the diagnostic log — title, class and owning process.
/// Text injection depends entirely on which window has focus, so being able to see that at
/// each step is usually what makes a failed insertion explainable.
/// </summary>
public static partial class WindowInfo
{
    public static string Describe(nint hwnd)
    {
        if (hwnd == 0) return "hwnd=0 (none)";

        try
        {
            return $"hwnd=0x{hwnd:X} '{GetTitle(hwnd)}' class={GetClassName(hwnd)} process={GetProcessName(hwnd)}";
        }
        catch (Exception ex)
        {
            return $"hwnd=0x{hwnd:X} (could not inspect: {ex.Message})";
        }
    }

    public static string DescribeForeground() => Describe(GetForegroundWindow());

    public static nint Foreground() => GetForegroundWindow();

    private static string GetTitle(nint hwnd)
    {
        var buffer = new StringBuilder(512);
        var length = GetWindowText(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : "(no title)";
    }

    private static string GetClassName(nint hwnd)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassName(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : "(unknown)";
    }

    private static string GetProcessName(nint hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0) return "(unknown)";

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return $"{process.ProcessName} ({processId})";
        }
        catch (Exception)
        {
            return $"pid {processId}";
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
