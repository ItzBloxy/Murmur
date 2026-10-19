namespace Murmur.Integration.Tests;

/// <summary>
/// Serializes every test that creates a whisper.cpp context.
///
/// xUnit runs test classes in parallel, which meant several Whisper and Silero VAD
/// contexts were being created and destroyed on different threads at once. The CPU backend
/// tolerated that; a GPU backend does not, and the test host died with an access violation
/// partway through the run.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WhisperGpuCollection
{
    public const string Name = "whisper-gpu";
}
