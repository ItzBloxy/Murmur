namespace Murmur.Core.Abstractions;

public interface IOverlay
{
    void Show();
    void SetPreview(string text);
    void SetTranscribing();
    void Hide();
}
