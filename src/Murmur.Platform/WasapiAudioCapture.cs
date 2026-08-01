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
public sealed class WasapiAudioCapture(IDiagnosticLog? log = null) : IAudioCapture
{
    private const int TargetSampleRate = 16000;

    /// <summary>
    /// WAVE_MAPPER. NAudio's <c>DeviceNumber</c> defaults to 0, which is the first
    /// enumerated device — not the one Windows is configured to use. That distinction
    /// matters on a machine with several microphones: recording from the wrong one yields
    /// silence, the speech gate correctly rejects it, and dictation fails with no error.
    /// </summary>
    private const int SystemDefaultDevice = -1;

    private readonly IDiagnosticLog _log = log ?? NullDiagnosticLog.Instance;

    private WaveInEvent? _capture;
    private volatile bool _stopRequested;
    private long _buffersSeen;
    private long _samplesSeen;
    private float _peak;

    public event Action<float[]>? FramesAvailable;
    public event Action<string>? Failed;

    /// <summary>Writes the input-device inventory to the log. Called once at startup.</summary>
    public void LogDeviceInventory()
    {
        try
        {
            _log.Write("audio", $"input devices: {WaveInEvent.DeviceCount}");
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                _log.Write("audio", $"  [{i}] {caps.ProductName} (channels={caps.Channels})");
            }

            _log.Write("audio", $"using DeviceNumber={SystemDefaultDevice} (WAVE_MAPPER = Windows default)");
        }
        catch (Exception ex)
        {
            _log.Write("audio", $"could not enumerate input devices: {ex.Message}");
        }
    }

    public void Start()
    {
        Stop();
        _stopRequested = false;
        _buffersSeen = 0;
        _samplesSeen = 0;
        _peak = 0f;

        var capture = new WaveInEvent
        {
            DeviceNumber = SystemDefaultDevice,
            WaveFormat = new WaveFormat(TargetSampleRate, 16, 1),
            BufferMilliseconds = 100,
        };

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;

        _capture = capture;
        capture.StartRecording();

        _log.Write("audio", $"capture started: {capture.WaveFormat} device={SystemDefaultDevice}");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_stopRequested) return;

        var samples = new float[e.BytesRecorded / 2];
        var peak = 0f;

        for (var i = 0; i < samples.Length; i++)
        {
            var sample = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
            samples[i] = sample;

            var magnitude = Math.Abs(sample);
            if (magnitude > peak) peak = magnitude;
        }

        _buffersSeen++;
        _samplesSeen += samples.Length;
        if (peak > _peak) _peak = peak;

        FramesAvailable?.Invoke(samples);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopRequested) return;
        _log.Write("audio", $"recording stopped unexpectedly: {e.Exception?.Message ?? "no exception"}");
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

        _log.Write("audio",
            $"capture stopped: {_buffersSeen} buffers, {_samplesSeen} samples " +
            $"({_samplesSeen / (double)TargetSampleRate:F2}s), peak amplitude {_peak:F4}" +
            (_peak < 0.001f ? "  <-- SILENT: wrong device, or the microphone is muted" : ""));
    }

    public void Dispose() => Stop();
}
