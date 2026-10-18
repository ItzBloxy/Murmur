using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class ModelStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "murmur-models", Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ModelStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Returns_the_cached_file_without_downloading()
    {
        var path = Path.Combine(_dir, "ggml-large-v3-turbo-q8_0.bin");
        await File.WriteAllBytesAsync(path, new byte[2048], Ct);
        var store = new ModelStore(_dir);
        var downloadCalled = false;

        var result = await store.EnsureAsync(
            "ggml-large-v3-turbo-q8_0.bin",
            _ => { downloadCalled = true; return Task.FromResult<Stream>(new MemoryStream()); },
            progress: null,
            Ct);

        Assert.Equal(path, result);
        Assert.False(downloadCalled);
    }

    [Fact]
    public async Task Downloads_when_missing_and_writes_atomically()
    {
        var store = new ModelStore(_dir);
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);

        var result = await store.EnsureAsync(
            "model.bin",
            _ => Task.FromResult<Stream>(new MemoryStream(payload)),
            progress: null,
            Ct);

        Assert.True(File.Exists(result));
        Assert.Equal(payload, await File.ReadAllBytesAsync(result, Ct));
        Assert.False(File.Exists(result + ".partial"), "the partial file must be cleaned up");
    }

    [Fact]
    public async Task Rejects_a_suspiciously_small_download()
    {
        var store = new ModelStore(_dir);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureAsync(
            "model.bin",
            _ => Task.FromResult<Stream>(new MemoryStream(new byte[10])),
            progress: null,
            Ct));

        Assert.False(File.Exists(Path.Combine(_dir, "model.bin")));
    }

    [Fact]
    public async Task A_leftover_partial_file_is_discarded_and_redownloaded()
    {
        var partial = Path.Combine(_dir, "model.bin.partial");
        await File.WriteAllBytesAsync(partial, new byte[123], Ct);
        var store = new ModelStore(_dir);
        var payload = new byte[4096];

        var result = await store.EnsureAsync(
            "model.bin",
            _ => Task.FromResult<Stream>(new MemoryStream(payload)),
            progress: null,
            Ct);

        Assert.Equal(4096, new FileInfo(result).Length);
        Assert.False(File.Exists(partial));
    }

    [Fact]
    public async Task Progress_is_reported_during_download()
    {
        var store = new ModelStore(_dir);
        var reports = new List<long>();

        await store.EnsureAsync(
            "model.bin",
            _ => Task.FromResult<Stream>(new MemoryStream(new byte[200_000])),
            progress: new Progress<long>(reports.Add),
            Ct);

        // Progress is reported asynchronously; give the synchronization context a chance.
        await Task.Delay(50, Ct);
        Assert.NotEmpty(reports);
    }

    [Fact]
    public void A_missing_file_is_not_cached()
    {
        Assert.False(new ModelStore(_dir).IsCached("model.bin"));
    }

    [Fact]
    public async Task A_plausible_file_is_cached()
    {
        await File.WriteAllBytesAsync(Path.Combine(_dir, "model.bin"), new byte[2048], Ct);

        Assert.True(new ModelStore(_dir).IsCached("model.bin"));
    }

    /// <summary>
    /// Must agree with <see cref="ModelStore.EnsureAsync"/>, which re-downloads a truncated
    /// file — otherwise startup would skip the download notice and then download anyway.
    /// </summary>
    [Fact]
    public async Task A_truncated_file_is_not_cached()
    {
        await File.WriteAllBytesAsync(Path.Combine(_dir, "model.bin"), new byte[10], Ct);

        Assert.False(new ModelStore(_dir).IsCached("model.bin"));
    }

    [Fact]
    public async Task A_partial_download_is_not_cached()
    {
        await File.WriteAllBytesAsync(Path.Combine(_dir, "model.bin.partial"), new byte[4096], Ct);

        Assert.False(new ModelStore(_dir).IsCached("model.bin"));
    }

    [Fact]
    public void A_missing_directory_is_not_cached()
    {
        Assert.False(new ModelStore(Path.Combine(_dir, "never-created")).IsCached("model.bin"));
    }

    /// <summary>
    /// Gates the startup balloon. Only the whisper model is large enough to be worth
    /// announcing; the VAD model is under 1 MB.
    /// </summary>
    [Fact]
    public async Task The_engine_needs_a_download_only_while_the_whisper_model_is_missing()
    {
        await using var engine = new WhisperEngine(new ModelStore(_dir), device: "cpu");
        Assert.True(engine.NeedsWhisperModelDownload);

        await File.WriteAllBytesAsync(Path.Combine(_dir, WhisperEngine.WhisperModelFileName), new byte[2048], Ct);

        Assert.False(engine.NeedsWhisperModelDownload);
    }
}
