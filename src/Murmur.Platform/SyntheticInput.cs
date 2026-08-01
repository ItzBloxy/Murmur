namespace Murmur.Platform;

/// <summary>
/// Marks keyboard input this application generates, so our own low-level hook can tell it
/// apart from a real keypress.
///
/// Without this the app triggers itself: inserting text synthesizes Ctrl (for the paste
/// chord) and key-ups for any held modifier, the hook observes them as genuine input, and
/// its held-key tracking desynchronizes from the physical keyboard. The visible result is
/// dictation restarting by itself the moment a transcript is inserted.
///
/// <c>dwExtraInfo</c> is the field Windows provides for exactly this purpose — it is
/// carried through SendInput untouched and surfaces in KBDLLHOOKSTRUCT.
/// </summary>
public static class SyntheticInput
{
    /// <summary>Arbitrary but distinctive: 'MRMR'.</summary>
    public const nuint Signature = 0x4D524D52;
}
