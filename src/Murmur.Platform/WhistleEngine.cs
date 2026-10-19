using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Murmur.Core;
using Murmur.Core.Abstractions;

namespace Murmur.Platform;

/// <summary>
/// Runs Cactus Compute's Whistle model through the needle engine they publish for Windows.
///
/// For windows-x86_64 Cactus ships needle.exe and a static library — no DLL — so each
/// transcription is one process: ~120 ms on the development machine, start-up and model
/// load included. Whistle is a 17 MB CPU model, so there is nothing resident to keep warm
/// and nothing on the GPU, which is why <see cref="IsLoaded"/> is always false.
///
/// Audio reaches the engine through a named pipe rather than a temporary file, so a
/// dictation never touches the disk.
/// </summary>
public sealed class WhistleEngine(ModelStore store, IDiagnosticLog? log = null) : ITranscriptionEngine
{
    // Pinned so the executable that runs is the one that was checked, not whatever the
    // repositories' main branches point at today. The short revision is in each file name,
    // so moving a pin downloads a fresh copy instead of reusing the old one.
    private const string EngineRevision = "d6c7d6a2e9655505e0f8e23339356dd1705900eb";
    private const string EngineSha256 = "0d4bc1da8c51f5600f5c4f4bb4a92e0abe142fae7b69e695ccab93e6745c883d";
    private const string EngineUrl =
        $"https://huggingface.co/Cactus-Compute/needle3/resolve/{EngineRevision}/windows-x86_64/needle.exe";

    private const string ModelRevision = "741118687e1f008f06d1351a402a731274789790";
    private const string ModelSha256 = "b6e02f048568ac5d01a2042556c658061e699acbc0aa2a1439f52f3d461dffeb";
    private const string ModelUrl =
        $"https://huggingface.co/Cactus-Compute/whistle/resolve/{ModelRevision}/whistle.cact";

    public const string EngineFileName = "needle-d6c7d6a2.exe";
    public const string ModelFileName = "whistle-74111868.cact";

    /// <summary>Engine plus model, for the download notice.</summary>
    public const int DownloadMegabytes = 18;

    /// <summary>Generous: five minutes of audio is ten 30 s windows at a fraction of a second each.</summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    private static readonly HttpClient Http = new();

    private readonly IDiagnosticLog _log = log ?? NullDiagnosticLog.Instance;
    private readonly SemaphoreSlim _download = new(1, 1);

    private string? _enginePath;
    private string? _modelPath;

    public bool IsLoaded => false;
    public bool IsPinned { get; private set; }

    /// <summary>Whether using Whistle will first fetch ~18 MB.</summary>
    public bool NeedsDownload => !store.IsCached(EngineFileName) || !store.IsCached(ModelFileName);

    /// <summary>Fetches the engine and model if they are not already cached. Safe to call repeatedly.</summary>
    public async Task EnsureDownloadedAsync(IProgress<long>? progress, CancellationToken cancellationToken)
    {
        // Serialized: a language picked from the menu and a hotkey press can both get here
        // at once, and two downloads must not share one .partial file.
        await _download.WaitAsync(cancellationToken);
        try
        {
            _enginePath = await store.EnsureAsync(
                EngineFileName, ct => Http.GetStreamAsync(EngineUrl, ct), EngineSha256, progress: null, cancellationToken);

            _modelPath = await store.EnsureAsync(
                ModelFileName, ct => Http.GetStreamAsync(ModelUrl, ct), ModelSha256, progress, cancellationToken);
        }
        finally
        {
            _download.Release();
        }
    }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        EnsureDownloadedAsync(progress: null, cancellationToken);

    /// <summary>Nothing stays resident between transcriptions.</summary>
    public Task UnloadAsync() => Task.CompletedTask;

    public void Pin() => IsPinned = true;
    public void Unpin() => IsPinned = false;

    /// <remarks>
    /// <paramref name="quality"/> only affects logging: needle always beam-searches, and at
    /// this speed a separate preview mode would buy nothing.
    /// </remarks>
    public async Task<IReadOnlyList<TranscribedSegment>> TranscribeAsync(
        float[] samples,
        string language,
        TranscriptionQuality quality,
        CancellationToken cancellationToken = default)
    {
        await EnsureDownloadedAsync(progress: null, cancellationToken);

        var started = Stopwatch.GetTimestamp();
        var output = WhistleOutput.Parse(await RunAsync(WaveFile.Encode(samples), language, cancellationToken));

        if (quality == TranscriptionQuality.Final)
        {
            _log.Write("whistle",
                $"language={output.Language} wall={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms " +
                $"ttft={output.TtftMs:F1} ms decode={output.DecodeTps:F0} tok/s");
        }

        // Whistle returns empty text for silence instead of inventing words, so there is no
        // no-speech probability to report. The VAD gate and the empty-text check cover it.
        return output.Text.Length == 0 ? [] : [new TranscribedSegment(output.Text, NoSpeechProbability: 0f)];
    }

    private async Task<string> RunAsync(byte[] wav, string language, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RunTimeout);
        var ct = deadline.Token;

        // CurrentUserOnly: this is the user's voice, and no other account may connect and read it.
        var pipeName = $"murmur-whistle-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(
            pipeName, PipeDirection.Out, maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var startInfo = new ProcessStartInfo(_enginePath!)
        {
            UseShellExecute = false,
            // A console window flashing up on every dictation would be absurd for a tray app.
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = store.Directory,
        };
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(_modelPath!);
        startInfo.ArgumentList.Add("--audio");
        startInfo.ArgumentList.Add($@"\\.\pipe\{pipeName}");
        startInfo.ArgumentList.Add("--audio-language");
        startInfo.ArgumentList.Add(language);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Whistle engine did not start.");

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            var connected = pipe.WaitForConnectionAsync(ct);
            var exited = process.WaitForExitAsync(ct);

            // An engine that fails before opening the audio never connects. Waiting on the
            // pipe alone would hang until the deadline instead of reporting its error.
            if (await Task.WhenAny(connected, exited) == connected)
            {
                await connected;
                try
                {
                    await pipe.WriteAsync(wav, ct);
                    pipe.WaitForPipeDrain();
                }
                catch (IOException)
                {
                    // The engine closed its end early. Its exit code and stderr say why.
                }
            }

            // Closing our end is the end-of-file the engine reads up to.
            await pipe.DisposeAsync();
            await exited;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Whistle did not finish within {RunTimeout.TotalMinutes:F0} minutes.");
        }
        finally
        {
            if (!process.HasExited) TryKill(process);
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Whistle failed (exit code {process.ExitCode}): {(await stderr).Trim()}");
        }

        return await stdout;
    }

    private void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Usually it exited between the HasExited check and the kill.
            _log.Write("whistle", $"could not stop the engine process: {ex.Message}");
        }
    }

    public ValueTask DisposeAsync()
    {
        _download.Dispose();
        return ValueTask.CompletedTask;
    }
}
