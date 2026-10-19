namespace Murmur.Core;

public enum SpeechModel
{
    /// <summary>whisper.cpp large-v3-turbo via Whisper.net, on the GPU when one is usable.</summary>
    Whisper,

    /// <summary>Cactus Compute's 17 MB CPU model, run through its needle engine.</summary>
    Whistle,
}

public sealed record SpeechLanguage(string Code, string DisplayName, SpeechModel Model);

/// <summary>
/// The languages the menu offers and the model each one runs on. Choosing a language
/// chooses its model, so there is no separate model setting to keep in sync.
/// </summary>
public static class SpeechLanguages
{
    /// <summary>
    /// Menu order. English stays on Whisper even though Whistle speaks it too: Whisper is
    /// what the app was tuned and measured against, and moving an existing language would
    /// change results for people who asked for nothing new.
    /// </summary>
    public static IReadOnlyList<SpeechLanguage> All { get; } =
    [
        new("en", "English", SpeechModel.Whisper),
        new("da", "Danish", SpeechModel.Whisper),
        new("de", "German", SpeechModel.Whistle),
        new("fr", "French", SpeechModel.Whistle),
        new("es", "Spanish", SpeechModel.Whistle),
        new("it", "Italian", SpeechModel.Whistle),
        new("nl", "Dutch", SpeechModel.Whistle),
        new("pl", "Polish", SpeechModel.Whistle),
    ];

    /// <summary>
    /// Unknown codes go to Whisper, which understands nearly every language — a hand-edited
    /// settings file naming one the menu does not offer still works.
    /// </summary>
    public static SpeechModel ModelFor(string code) =>
        All.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.Model
        ?? SpeechModel.Whisper;
}
