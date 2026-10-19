namespace Murmur.Core.Abstractions;

public enum InjectionResult
{
    Injected,
    /// <summary>Focus moved during dictation, so the text went to the clipboard instead.</summary>
    CopiedToClipboardFocusChanged,
    Failed,
}

public interface ITextInjector
{
    /// <summary>The window handle to remember at record start.</summary>
    nint GetForegroundWindow();

    /// <param name="expectedWindow">
    /// The handle captured at record start. If the foreground window no longer matches,
    /// the implementation must not type — it copies to the clipboard instead.
    /// </param>
    InjectionResult Inject(string text, nint expectedWindow);
}
