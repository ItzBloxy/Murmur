namespace Murmur.Core.Abstractions;

public interface INotifier
{
    void Notify(string title, string message);
    void SetState(DictationState state, bool modelLoaded, bool modelPinned);
}
