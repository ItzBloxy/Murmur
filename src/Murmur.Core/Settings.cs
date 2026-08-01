namespace Murmur.Core;

/// <summary>Persisted user configuration. Defaults match the spec.</summary>
public sealed record Settings
{
    /// <summary>Whisper language code: "da" or "en".</summary>
    public string Language { get; init; } = "da";

    /// <summary>Name of a <c>System.Windows.Forms.Keys</c> value, e.g. "RControlKey".</summary>
    public string Hotkey { get; init; } = "RControlKey";

    public int HoldThresholdMs { get; init; } = 400;
    public int IdleUnloadMinutes { get; init; } = 5;
    public int MaxRecordingMinutes { get; init; } = 5;
    public int VadSilenceMs { get; init; } = 500;
    public bool StartWithWindows { get; init; }

    /// <summary>"cuda" or "cpu".</summary>
    public string Device { get; init; } = "cuda";
}
