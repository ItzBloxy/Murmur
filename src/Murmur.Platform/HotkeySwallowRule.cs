using System.Windows.Forms;

namespace Murmur.Platform;

/// <summary>
/// Decides whether the hook consumes the hotkey or lets it reach the focused application.
///
/// Modifier-only bindings pass through: a lone Right-Ctrl press is inert in virtually every
/// application, and swallowing it would break combinations like Right-Ctrl+C. Normal keys
/// are swallowed so they do not type a character or fire an application shortcut.
/// </summary>
public static class HotkeySwallowRule
{
    public const Keys Default = Keys.RControlKey;

    private static readonly HashSet<Keys> ModifierKeys =
    [
        Keys.LControlKey, Keys.RControlKey,
        Keys.LShiftKey, Keys.RShiftKey,
        Keys.LMenu, Keys.RMenu,
        Keys.LWin, Keys.RWin,
    ];

    public static bool ShouldSwallow(Keys key) => !ModifierKeys.Contains(key);

    public static Keys Parse(string name) =>
        Enum.TryParse<Keys>(name, ignoreCase: true, out var key) && Enum.IsDefined(key)
            ? key
            : Default;
}
