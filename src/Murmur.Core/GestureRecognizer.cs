namespace Murmur.Core;

/// <summary>A raw key transition for the bound hotkey, as reported by the platform hook.</summary>
public enum KeyTransition
{
    Down,
    Up,
    /// <summary>An auto-repeat key-down while the key is already held. Always ignored.</summary>
    Repeat,
}

public enum GestureState
{
    Idle,
    /// <summary>Recording, but we do not yet know whether this is a tap or a hold.</summary>
    Undetermined,
    /// <summary>Recording, latched on by a tap. Ends on the next key-down.</summary>
    Toggled,
}

public enum GestureAction
{
    None,
    StartRecording,
    StopRecording,
}

/// <summary>
/// Turns raw key transitions into recording commands.
///
/// Recording starts on key-<em>down</em> in both gestures, so no audio is lost while we are
/// still determining which gesture is happening. A release before the threshold is a tap
/// and latches recording on; a release at or after the threshold is a hold and ends it.
/// </summary>
public sealed class GestureRecognizer(int holdThresholdMs)
{
    private long _downAtMs;

    public GestureState State { get; private set; } = GestureState.Idle;

    public void Reset() => State = GestureState.Idle;

    public GestureAction Handle(KeyTransition transition, long timestampMs)
    {
        if (transition == KeyTransition.Repeat) return GestureAction.None;

        switch (State)
        {
            case GestureState.Idle:
                if (transition != KeyTransition.Down) return GestureAction.None;
                _downAtMs = timestampMs;
                State = GestureState.Undetermined;
                return GestureAction.StartRecording;

            case GestureState.Undetermined:
                if (transition != KeyTransition.Up) return GestureAction.None;
                if (timestampMs - _downAtMs < holdThresholdMs)
                {
                    State = GestureState.Toggled;
                    return GestureAction.None;
                }
                State = GestureState.Idle;
                return GestureAction.StopRecording;

            case GestureState.Toggled:
                if (transition != KeyTransition.Down) return GestureAction.None;
                State = GestureState.Idle;
                return GestureAction.StopRecording;

            default:
                return GestureAction.None;
        }
    }
}
