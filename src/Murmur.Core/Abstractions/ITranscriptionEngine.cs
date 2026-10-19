namespace Murmur.Core.Abstractions;

public enum TranscriptionQuality
{
    /// <summary>Greedy decoding for the live overlay. Speed over accuracy.</summary>
    Preview,
    /// <summary>Beam search over the whole buffer. The text the user actually receives.</summary>
    Final,
}

public interface ITranscriptionEngine : IAsyncDisposable
{
    bool IsLoaded { get; }

    /// <summary>True while the idle-unload timer is suspended by an explicit Preload.</summary>
    bool IsPinned { get; }

    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);
    Task UnloadAsync();

    void Pin();
    void Unpin();

    Task<IReadOnlyList<TranscribedSegment>> TranscribeAsync(
        float[] samples,
        string language,
        TranscriptionQuality quality,
        CancellationToken cancellationToken = default);
}
