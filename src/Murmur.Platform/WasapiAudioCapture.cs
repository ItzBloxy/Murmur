using Murmur.Core.Abstractions;
using NAudio.Wave;

namespace Murmur.Platform;

/// <summary>
/// Captures the default input device and resamples to the 16 kHz mono float32 Whisper
/// expects. Reports device loss through <see cref="Failed"/> rather than throwing on a
/// background thread.
/// </summary>
public sealed class WasapiAudioCapture : IAudioCapture
{
    private const int TargetSampleRate = 16000;

    private WaveInEvent? _capture;
    private BufferedWaveProvider? _buffer;
    private MediaFoundationResampler? _resampler;
    private bool _stopRequested;

    public event Action<float[]>? FramesAvailable;
    public event Action<string>? Failed;

    public void Start()
    {
        Stop();
        _stopRequested = false;

        var capture = new WaveInEvent
        {
            WaveFormat = new WaveFormat(44100, 16, 1),
            BufferMilliseconds = 100,
        };

        _buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5),
        };

        _resampler = new MediaFoundationResampler(
            _buffer, new WaveFormat(TargetSampleRate, 16, 1))
        {
            ResamplerQuality = 60,
        };

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;

        _capture = capture;
        capture.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_buffer is null || _resampler is null) return;

        try
        {
            _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            // Drain whatever the resampler can produce right now.
            var bytes = new byte[8192];
            int read;
            while ((read = _resampler.Read(bytes, 0, bytes.Length)) > 0)
            {
                var samples = new float[read / 2];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
                }

                FramesAvailable?.Invoke(samples);
                if (read < bytes.Length) break;
            }
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex.Message);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopRequested) return;
        Failed?.Invoke(e.Exception?.Message ?? "The input device stopped unexpectedly.");
    }

    public void Stop()
    {
        _stopRequested = true;

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try { _capture.StopRecording(); } catch (Exception) { /* already gone */ }
            _capture.Dispose();
            _capture = null;
        }

        _resampler?.Dispose();
        _resampler = null;
        _buffer = null;
    }

    public void Dispose() => Stop();
}
