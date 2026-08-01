namespace Murmur.Core.Abstractions;

/// <summary>Captures the default input device as 16 kHz mono float32.</summary>
public interface IAudioCapture : IDisposable
{
    event Action<float[]>? FramesAvailable;

    /// <summary>Fires when capture breaks mid-recording, e.g. the microphone was unplugged.</summary>
    event Action<string>? Failed;

    void Start();
    void Stop();
}
