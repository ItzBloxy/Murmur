namespace Murmur.Core.Abstractions;

/// <summary>
/// Reports raw key transitions for the bound hotkey. Deliberately does not interpret
/// gestures — that logic lives in <see cref="GestureRecognizer"/> so it can be unit-tested.
/// </summary>
public interface IHotkeySource : IDisposable
{
    /// <summary>Fires with the transition and a monotonic timestamp in milliseconds.</summary>
    event Action<KeyTransition, long>? KeyEvent;

    void Start();

    /// <param name="hotkey">Name of a <c>System.Windows.Forms.Keys</c> value.</param>
    void Rebind(string hotkey);
}
