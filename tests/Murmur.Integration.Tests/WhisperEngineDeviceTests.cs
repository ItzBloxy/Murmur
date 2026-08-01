using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class WhisperEngineDeviceTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("")]
    public void Auto_prefers_the_gpu(string device)
    {
        Assert.True(WhisperEngine.PrefersGpu(device));
    }

    /// <summary>
    /// Settings files written before the rename say "cuda". They must keep preferring the
    /// GPU rather than silently dropping to the CPU path, which would be a 40x regression
    /// with no visible cause.
    /// </summary>
    [Theory]
    [InlineData("cuda")]
    [InlineData("Cuda")]
    public void Legacy_cuda_still_prefers_the_gpu(string device)
    {
        Assert.True(WhisperEngine.PrefersGpu(device));
    }

    [Theory]
    [InlineData("cpu")]
    [InlineData("CPU")]
    [InlineData("Cpu")]
    public void Cpu_is_the_only_value_that_declines_the_gpu(string device)
    {
        Assert.False(WhisperEngine.PrefersGpu(device));
    }
}
