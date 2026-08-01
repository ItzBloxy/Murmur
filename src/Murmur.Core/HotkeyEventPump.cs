using System.Threading.Channels;

namespace Murmur.Core;

/// <summary>
/// Decouples the keyboard hook callback from everything the hotkey triggers.
///
/// This exists because of two hard constraints on a <c>WH_KEYBOARD_LL</c> callback:
///
/// 1. Windows silently removes a hook whose callback overruns <c>LowLevelHooksTimeout</c>.
///    Our stored hook handle stays non-zero when that happens, so nothing detects it — the
///    hotkey simply stops working for the rest of the session.
/// 2. The callback runs on the hook's own thread, which is STA. COM objects created there
///    and then used from a thread-pool thread fault with an access violation.
///
/// <see cref="Post"/> therefore does nothing but enqueue, and every handler runs on a
/// pooled worker instead.
/// </summary>
public sealed class HotkeyEventPump : IDisposable
{
    private readonly Channel<(KeyTransition Transition, long TimestampMs)> _channel =
        Channel.CreateUnbounded<(KeyTransition, long)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            // Without this the writer may run the reader's continuation inline, which would
            // put the handler straight back onto the hook thread and defeat the point.
            AllowSynchronousContinuations = false,
        });

    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _consumer;

    public HotkeyEventPump() => _consumer = Task.Run(ConsumeAsync);

    public event Action<KeyTransition, long>? KeyEvent;

    /// <summary>Never blocks and never throws. Safe to call from the hook callback.</summary>
    public void Post(KeyTransition transition, long timestampMs) =>
        _channel.Writer.TryWrite((transition, timestampMs));

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var e in _channel.Reader.ReadAllAsync(_shutdown.Token))
            {
                try
                {
                    KeyEvent?.Invoke(e.Transition, e.TimestampMs);
                }
                catch (Exception)
                {
                    // A faulting handler must not take the pump down with it — that would
                    // disable the hotkey for the rest of the session.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _shutdown.Cancel();

        try { _consumer.Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception) { /* nothing useful to do while tearing down */ }

        _shutdown.Dispose();
    }
}
