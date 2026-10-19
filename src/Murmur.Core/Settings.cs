namespace Murmur.Core;

/// <summary>Persisted user configuration. Defaults match the spec.</summary>
public sealed record Settings
{
    /// <summary>
    /// A code from <see cref="SpeechLanguages"/>: "en" or "da" run on Whisper; "de", "fr",
    /// "es", "it", "nl" or "pl" run on Whistle. The language decides the model.
    /// </summary>
    public string Language { get; init; } = "da";

    /// <summary>Name of a <c>System.Windows.Forms.Keys</c> value, e.g. "RControlKey".</summary>
    public string Hotkey { get; init; } = "RControlKey";

    /// <summary>
    /// How long the key must be held before the press counts as push-to-talk rather than a
    /// tap. Deliberately generous: misreading a tap as a hold records for a fraction of a
    /// second and silently discards the dictation, while misreading a hold as a tap merely
    /// leaves recording running, which the overlay makes obvious and another tap fixes.
    /// </summary>
    public int HoldThresholdMs { get; init; } = 700;
    public int IdleUnloadMinutes { get; init; } = 5;
    public int MaxRecordingMinutes { get; init; } = 5;
    public int VadSilenceMs { get; init; } = 500;
    public bool StartWithWindows { get; init; }

    /// <summary>
    /// "auto" or "cpu". "auto" lets Whisper.net load the best backend available — CUDA if
    /// its runtime is present, otherwise Vulkan, otherwise CPU. It deliberately does not
    /// name a backend: the setting records a preference, and only the "backend=" line in
    /// the log says what actually loaded. A legacy value of "cuda" is read as "auto".
    /// </summary>
    public string Device { get; init; } = "auto";

    /// <summary>
    /// "onStop" inserts the whole dictation once, transcribed in a single pass — Whisper
    /// sees the entire utterance, which is what gives it consistent punctuation,
    /// capitalisation and proper-noun spelling.
    ///
    /// "asYouSpeak" inserts each phrase as you pause, so text appears while you talk. Each
    /// phrase is transcribed alone, so cross-phrase context is lost. Nothing is ever
    /// rewritten or deleted in either mode.
    /// </summary>
    public string InsertMode { get; init; } = "onStop";
}
