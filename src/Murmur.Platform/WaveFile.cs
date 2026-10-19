using System.Buffers.Binary;

namespace Murmur.Platform;

/// <summary>
/// Encodes capture samples as a 16 kHz mono 16-bit PCM WAV. 16-bit rather than float
/// because it is the one format every WAV reader handles, and for speech recognition the
/// quantization is far below the microphone's own noise floor.
/// </summary>
public static class WaveFile
{
    public const int SampleRate = 16000;

    private const int HeaderBytes = 44;

    public static byte[] Encode(float[] samples)
    {
        var dataBytes = samples.Length * 2;
        var bytes = new byte[HeaderBytes + dataBytes];
        var span = bytes.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], HeaderBytes - 8 + dataBytes);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);              // fmt chunk size
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);               // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);               // mono
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * 2);  // byte rate
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);               // block align
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);              // bits per sample
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);

        for (var i = 0; i < samples.Length; i++)
        {
            // Clipped, not wrapped: a hot microphone can exceed ±1, and a wrapped peak is a click.
            var value = Math.Clamp(MathF.Round(samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(span[(HeaderBytes + i * 2)..], (short)value);
        }

        return bytes;
    }
}
