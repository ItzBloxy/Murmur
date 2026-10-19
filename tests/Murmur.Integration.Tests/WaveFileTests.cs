using System.Buffers.Binary;
using System.Text;
using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class WaveFileTests
{
    [Fact]
    public void Header_describes_16_kHz_mono_16_bit_pcm()
    {
        var bytes = WaveFile.Encode(new float[100]);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(bytes.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(bytes, 8, 8));
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(20)));      // PCM
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22)));      // mono
        Assert.Equal(16000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)));
        Assert.Equal(32000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28)));  // byte rate
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(32)));      // block align
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34)));     // bits
        Assert.Equal("data", Encoding.ASCII.GetString(bytes, 36, 4));
        Assert.Equal(200, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40)));
        Assert.Equal(44 + 200, bytes.Length);
    }

    [Fact]
    public void Samples_are_scaled_to_16_bit()
    {
        var bytes = WaveFile.Encode([0f, 0.5f, -0.5f, 1f, -1f]);

        Assert.Equal([0, 16384, -16384, 32767, -32767], ReadSamples(bytes));
    }

    /// <summary>A hot microphone can exceed ±1. Wrapping would turn a peak into a click.</summary>
    [Fact]
    public void Out_of_range_samples_are_clipped_not_wrapped()
    {
        var bytes = WaveFile.Encode([2f, -2f]);

        Assert.Equal([32767, -32768], ReadSamples(bytes));
    }

    private static short[] ReadSamples(byte[] wav)
    {
        var samples = new short[(wav.Length - 44) / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + i * 2));
        }

        return samples;
    }
}
