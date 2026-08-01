using System.Diagnostics;
using Murmur.Core;

namespace Murmur.Core.Tests;

public sealed class HotkeyEventPumpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The reason this class exists. A low-level keyboard hook callback that takes too long
    /// is silently removed by Windows, killing the hotkey for the rest of the session — and
    /// the callback also inherits the hook thread's STA apartment, which crashed COM audio
    /// objects created on it. Posting must therefore never block, no matter what the
    /// handler does.
    /// </summary>
    [Fact]
    public void Post_returns_immediately_even_when_the_handler_blocks()
    {
        using var pump = new HotkeyEventPump();
        var handlerStarted = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();

        pump.KeyEvent += (_, _) =>
        {
            handlerStarted.Set();
            release.Wait(TimeSpan.FromSeconds(5), Ct);
        };

        pump.Post(KeyTransition.Down, 0);
        Assert.True(handlerStarted.Wait(TimeSpan.FromSeconds(2), Ct), "handler never ran");

        // The handler is now blocked. Posting again must still return at once.
        var stopwatch = Stopwatch.StartNew();
        pump.Post(KeyTransition.Up, 1);
        stopwatch.Stop();

        release.Set();
        Assert.True(stopwatch.ElapsedMilliseconds < 50,
            $"Post blocked for {stopwatch.ElapsedMilliseconds}ms; the hook callback would have been unhooked.");
    }

    [Fact]
    public async Task Events_are_raised_off_the_posting_thread()
    {
        using var pump = new HotkeyEventPump();
        var observed = new TaskCompletionSource<int>();

        pump.KeyEvent += (_, _) => observed.TrySetResult(Environment.CurrentManagedThreadId);

        // Captured before the await: afterwards the continuation may resume on any pool
        // thread, including the pump's own, which made this assertion intermittently pass
        // for the wrong reason.
        var postingThreadId = Environment.CurrentManagedThreadId;
        pump.Post(KeyTransition.Down, 0);

        var handlerThreadId = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2), Ct);
        Assert.NotEqual(postingThreadId, handlerThreadId);
    }

    [Fact]
    public void Events_are_delivered_in_order()
    {
        using var pump = new HotkeyEventPump();
        var seen = new List<KeyTransition>();
        var done = new ManualResetEventSlim();

        pump.KeyEvent += (transition, _) =>
        {
            lock (seen)
            {
                seen.Add(transition);
                if (seen.Count == 4) done.Set();
            }
        };

        pump.Post(KeyTransition.Down, 0);
        pump.Post(KeyTransition.Repeat, 1);
        pump.Post(KeyTransition.Up, 2);
        pump.Post(KeyTransition.Down, 3);

        Assert.True(done.Wait(TimeSpan.FromSeconds(2), Ct), "not all events arrived");
        lock (seen)
        {
            Assert.Equal(
                [KeyTransition.Down, KeyTransition.Repeat, KeyTransition.Up, KeyTransition.Down],
                seen);
        }
    }

    /// <summary>A faulting handler must not silently kill the hotkey for the session.</summary>
    [Fact]
    public void A_throwing_handler_does_not_stop_later_events()
    {
        using var pump = new HotkeyEventPump();
        var secondArrived = new ManualResetEventSlim();
        var first = true;

        pump.KeyEvent += (_, _) =>
        {
            if (first)
            {
                first = false;
                throw new InvalidOperationException("boom");
            }

            secondArrived.Set();
        };

        pump.Post(KeyTransition.Down, 0);
        pump.Post(KeyTransition.Up, 1);

        Assert.True(secondArrived.Wait(TimeSpan.FromSeconds(2), Ct),
            "the pump stopped delivering after a handler threw");
    }

    [Fact]
    public void Posting_after_dispose_is_harmless()
    {
        var pump = new HotkeyEventPump();
        pump.Dispose();

        pump.Post(KeyTransition.Down, 0);   // must not throw
    }
}
