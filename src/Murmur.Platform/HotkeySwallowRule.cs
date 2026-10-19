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
        Keys.LControlKey, Keys.RControlKey, Keys.ControlKey,
        Keys.LShiftKey, Keys.RShiftKey, Keys.ShiftKey,
        Keys.LMenu, Keys.RMenu, Keys.Menu,
        Keys.LWin, Keys.RWin,
    ];

    public static bool ShouldSwallow(Keys key) => !ModifierKeys.Contains(key);

    /// <summary>
    /// Whether a hook-reported key satisfies the configured binding.
    ///
    /// These come from two different virtual-key namespaces. The rebinding dialog reads
    /// window messages, which report the generic <c>VK_CONTROL</c> / <c>VK_SHIFT</c> /
    /// <c>VK_MENU</c> with no side information, while <c>WH_KEYBOARD_LL</c> always reports
    /// the side-specific code. Comparing them directly means a modifier bound in the dialog
    /// never matches anything the hook delivers, and the hotkey silently does nothing.
    ///
    /// Treating a generic binding as "either side" also happens to be what a user means
    /// when they choose Ctrl.
    /// </summary>
    public static bool Matches(Keys bound, Keys reported)
    {
        if (bound == reported) return true;

        return bound switch
        {
            Keys.ControlKey => reported is Keys.LControlKey or Keys.RControlKey,
            Keys.ShiftKey => reported is Keys.LShiftKey or Keys.RShiftKey,
            Keys.Menu => reported is Keys.LMenu or Keys.RMenu,
            _ => false,
        };
    }

    /// <summary>
    /// Turns a generic modifier into the side actually pressed.
    ///
    /// A rebinding dialog reads virtual-key codes from window messages, which report
    /// <c>VK_CONTROL</c> for either Ctrl. Storing that verbatim means the binding matches
    /// both sides, so a user who deliberately pressed Right Ctrl finds Left Ctrl triggering
    /// too. Probing which side is physically down at capture time recovers the distinction.
    /// </summary>
    /// <param name="isDown">Reports whether a specific virtual key is currently held.</param>
    public static Keys ResolveSide(Keys key, Func<Keys, bool> isDown)
    {
        return key switch
        {
            Keys.ControlKey => Pick(Keys.LControlKey, Keys.RControlKey),
            Keys.ShiftKey => Pick(Keys.LShiftKey, Keys.RShiftKey),
            Keys.Menu => Pick(Keys.LMenu, Keys.RMenu),
            _ => key,
        };

        // Falls back to the generic key if neither side reports as down, which still
        // matches either side rather than silently binding to nothing.
        Keys Pick(Keys left, Keys right) =>
            isDown(right) ? right : isDown(left) ? left : key;
    }

    public static Keys Parse(string name) =>
        Enum.TryParse<Keys>(name, ignoreCase: true, out var key) && Enum.IsDefined(key)
            ? key
            : Default;
}
