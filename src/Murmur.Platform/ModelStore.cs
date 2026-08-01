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

    /// <param name="openStream">Fetches the model. Called only when the file is not cached.</param>
    /// <param name="progress">Receives cumulative bytes written, if supplied.</param>
    public async Task<string> EnsureAsync(
        string fileName,
        Func<CancellationToken, Task<Stream>> openStream,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        System.IO.Directory.CreateDirectory(directory);

        var finalPath = PathFor(fileName);
        if (File.Exists(finalPath) && new FileInfo(finalPath).Length >= MinimumPlausibleModelBytes)
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
