using Murmur.Core.Abstractions;
using NAudio.Wave;

namespace Murmur.Platform;

/// <summary>
/// Captures the default input device as the 16 kHz mono float32 Whisper expects.
///
/// The device is opened at 16 kHz directly rather than captured at 44.1 kHz and resampled.
/// An earlier version used <c>MediaFoundationResampler</c> and crashed the process with an
/// access violation: the resampler is a COM object, it was constructed on the STA keyboard-hook
/// thread, and NAudio then called into it from an MTA thread-pool thread. Asking the driver
/// for the rate we want removes the COM object, the apartment mismatch, and a dispose race
/// between <see cref="Stop"/> and an in-flight read.
/// </summary>
public sealed class WasapiAudioCapture : IAudioCapture
{
    private const int TargetSampleRate = 16000;

    private WaveInEvent? _capture;
    private volatile bool _stopRequested;

    public event Action<float[]>? FramesAvailable;
    public event Action<string>? Failed;

    public void Start()
    {
        Stop();
        _stopRequested = false;

        var capture = new WaveInEvent
        {
            WaveFormat = new WaveFormat(TargetSampleRate, 16, 1),
            BufferMilliseconds = 100,
        };

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;

        _capture = capture;
        capture.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_stopRequested) return;

        var samples = new float[e.BytesRecorded / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
        }

        FramesAvailable?.Invoke(samples);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopRequested) return;
        Failed?.Invoke(e.Exception?.Message ?? "The input device stopped unexpectedly.");
    }

    public void Stop()
    {
        _stopRequested = true;

        var capture = _capture;
        _capture = null;
        if (capture is null) return;

        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;

        try { capture.StopRecording(); }
        catch (Exception) { /* device already gone */ }

        capture.Dispose();
    }

    public void Dispose() => Stop();
}
