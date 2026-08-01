namespace Murmur.Core.Abstractions;

public interface IStartupRegistrar
{
    bool IsEnabled { get; }
    void Enable(string executablePath);
    void Disable();
}
