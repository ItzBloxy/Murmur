using Microsoft.Win32;
using Murmur.Core.Abstractions;

namespace Murmur.Platform;

/// <summary>Per-user Run key. No elevation required.</summary>
public sealed class RunKeyStartupRegistrar(string valueName = "Murmur") : IStartupRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(valueName) is not null;
        }
    }

    public void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(valueName, $"\"{executablePath}\"");
    }

    /// <summary>The executable the Run key launches, or null when autostart is off.</summary>
    public string? RegisteredPath
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return (key?.GetValue(valueName) as string)?.Trim('"');
        }
    }

    /// <summary>
    /// Re-points autostart at <paramref name="executablePath"/> when the registered file no
    /// longer exists — a single-file exe gets downloaded, then moved or renamed. A
    /// registration that still resolves is left alone, so a second copy such as a dev
    /// build cannot take autostart away from the one the user installed.
    /// </summary>
    /// <returns>Whether the registration was changed.</returns>
    public bool RepairIfMoved(string executablePath)
    {
        if (RegisteredPath is not { } registered || File.Exists(registered)) return false;

        Enable(executablePath);
        return true;
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
