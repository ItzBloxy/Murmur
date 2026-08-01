using Murmur.Core;

namespace Murmur.Core.Tests;

public sealed class GestureRecognizerTests
{
    private static GestureRecognizer Recognizer() => new(holdThresholdMs: 400);

    [Fact]
    public void Key_down_from_idle_starts_recording()
    {
        var r = Recognizer();

        var action = r.Handle(KeyTransition.Down, 0);

        Assert.Equal(GestureAction.StartRecording, action);
        Assert.Equal(GestureState.Undetermined, r.State);
    }

    [Fact]
    public void Key_up_in_idle_is_ignored()
    {
        var r = Recognizer();

        var action = r.Handle(KeyTransition.Up, 0);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void Quick_release_is_a_tap_and_keeps_recording()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 1000);

        var action = r.Handle(KeyTransition.Up, 1399);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(GestureState.Toggled, r.State);
    }

    [Fact]
    public void Release_at_the_threshold_is_a_hold_and_stops()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 1000);

        var action = r.Handle(KeyTransition.Up, 1400);

        Assert.Equal(GestureAction.StopRecording, action);
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void Long_release_is_a_hold_and_stops()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 1000);

        var action = r.Handle(KeyTransition.Up, 5000);

        Assert.Equal(GestureAction.StopRecording, action);
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void Second_tap_stops_on_key_down_not_key_up()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 0);
        r.Handle(KeyTransition.Up, 100);   // tap -> Toggled

        var action = r.Handle(KeyTransition.Down, 3000);

        Assert.Equal(GestureAction.StopRecording, action);
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void Trailing_key_up_after_a_toggle_stop_is_ignored()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 0);
        r.Handle(KeyTransition.Up, 100);     // tap -> Toggled
        r.Handle(KeyTransition.Down, 3000);  // stop -> Idle

        var action = r.Handle(KeyTransition.Up, 3050);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(GestureState.Idle, r.State);
    }

    [Fact]
    public void Key_up_in_toggled_is_ignored()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 0);
        r.Handle(KeyTransition.Up, 100);   // tap -> Toggled

        var action = r.Handle(KeyTransition.Up, 200);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(GestureState.Toggled, r.State);
    }

    [Theory]
    [InlineData(GestureState.Idle)]
    [InlineData(GestureState.Undetermined)]
    [InlineData(GestureState.Toggled)]
    public void Key_repeat_never_changes_state(GestureState target)
    {
        var r = Recognizer();
        if (target is GestureState.Undetermined or GestureState.Toggled) r.Handle(KeyTransition.Down, 0);
        if (target is GestureState.Toggled) r.Handle(KeyTransition.Up, 50);
        Assert.Equal(target, r.State);

        var action = r.Handle(KeyTransition.Repeat, 900);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(target, r.State);
    }

    [Fact]
    public void Repeats_do_not_extend_the_hold_measurement()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 1000);
        r.Handle(KeyTransition.Repeat, 1200);
        r.Handle(KeyTransition.Repeat, 1300);

        var action = r.Handle(KeyTransition.Up, 1399);

        Assert.Equal(GestureAction.None, action);
        Assert.Equal(GestureState.Toggled, r.State);
    }

    [Fact]
    public void A_full_tap_cycle_can_repeat()
    {
        var r = Recognizer();

        Assert.Equal(GestureAction.StartRecording, r.Handle(KeyTransition.Down, 0));
        Assert.Equal(GestureAction.None, r.Handle(KeyTransition.Up, 50));
        Assert.Equal(GestureAction.StopRecording, r.Handle(KeyTransition.Down, 2000));
        Assert.Equal(GestureAction.None, r.Handle(KeyTransition.Up, 2050));
        Assert.Equal(GestureAction.StartRecording, r.Handle(KeyTransition.Down, 4000));
    }

    [Fact]
    public void Reset_returns_to_idle_from_any_state()
    {
        var r = Recognizer();
        r.Handle(KeyTransition.Down, 0);
        r.Handle(KeyTransition.Up, 50);
        Assert.Equal(GestureState.Toggled, r.State);

        r.Reset();

        Assert.Equal(GestureState.Idle, r.State);
        Assert.Equal(GestureAction.StartRecording, r.Handle(KeyTransition.Down, 1000));
    }
}
