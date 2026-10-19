using System.Security.Cryptography;

namespace Murmur.Platform;

/// <summary>
/// Caches GGML model files on disk. Downloads land in a <c>.partial</c> file that is only
/// moved into place once complete, so an interrupted download can never be mistaken for a
/// usable model.
/// </summary>
public sealed class ModelStore(string directory)
{
    /// <summary>Anything smaller than this is an error page or a truncated transfer, not a model.</summary>
    private const long MinimumPlausibleModelBytes = 1024;

    public string Directory => directory;

    public string PathFor(string fileName) => System.IO.Path.Combine(directory, fileName);

    /// <summary>Whether <see cref="EnsureAsync"/> would return without downloading.</summary>
    public bool IsCached(string fileName)
    {
        var path = PathFor(fileName);
        return File.Exists(path) && new FileInfo(path).Length >= MinimumPlausibleModelBytes;
    }

    /// <param name="openStream">Fetches the model. Called only when the file is not cached.</param>
    /// <param name="progress">Receives cumulative bytes written, if supplied.</param>
    public Task<string> EnsureAsync(
        string fileName,
        Func<CancellationToken, Task<Stream>> openStream,
        IProgress<long>? progress,
        CancellationToken cancellationToken) =>
        EnsureAsync(fileName, openStream, expectedSha256: null, progress, cancellationToken);

    /// <param name="expectedSha256">
    /// Lowercase hex. When given, a download that does not match is discarded before it
    /// reaches the final path. Checked on download only — the cached copy is trusted, which
    /// is why pinned files carry their revision in the file name.
    /// </param>
    public async Task<string> EnsureAsync(
        string fileName,
        Func<CancellationToken, Task<Stream>> openStream,
        string? expectedSha256,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        System.IO.Directory.CreateDirectory(directory);

        var finalPath = PathFor(fileName);
        if (IsCached(fileName))
        {
            return finalPath;
        }

        var partialPath = finalPath + ".partial";
        if (File.Exists(partialPath)) File.Delete(partialPath);

        try
        {
            await using (var source = await openStream(cancellationToken))
            await using (var destination = File.Create(partialPath))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    progress?.Report(written);
                }
            }

            if (new FileInfo(partialPath).Length < MinimumPlausibleModelBytes)
            {
                throw new InvalidDataException(
                    $"Downloaded model '{fileName}' is only {new FileInfo(partialPath).Length} bytes — the transfer failed.");
            }

            if (expectedSha256 is not null)
            {
                await using var downloaded = File.OpenRead(partialPath);
                var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(downloaded, cancellationToken));

                if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Downloaded '{fileName}' failed its integrity check (sha256 {actual}, expected {expectedSha256}).");
                }
            }

            File.Move(partialPath, finalPath, overwrite: true);
            return finalPath;
        }
        catch
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
            throw;
        }
    }
}
