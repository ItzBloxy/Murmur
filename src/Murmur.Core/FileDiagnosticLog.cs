using System.Text;
using Murmur.Core.Abstractions;

namespace Murmur.Core;

/// <summary>
/// Appends to a single plain-text log so the whole history can be handed over in one file.
/// Rolls over at <see cref="MaxBytes"/> so it cannot grow without bound.
/// </summary>
public sealed class FileDiagnosticLog : IDiagnosticLog
{
    private const long MaxBytes = 4 * 1024 * 1024;

    private readonly Lock _sync = new();

    public FileDiagnosticLog(string path)
    {
        Path = path;

        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);

        RollIfTooLarge();
    }

    public string Path { get; }

    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? ".";

    /// <summary>Writes a banner so separate runs are easy to tell apart in one file.</summary>
    public void WriteSessionHeader(IEnumerable<(string Key, string Value)> facts)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("==============================================================");
        builder.AppendLine($"  SESSION START  {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine("==============================================================");

        foreach (var (key, value) in facts)
        {
            builder.AppendLine($"  {key,-22}: {value}");
        }

        builder.AppendLine("--------------------------------------------------------------");

        Append(builder.ToString());
    }

    public void Write(string category, string message) =>
        Append($"{DateTime.Now:HH:mm:ss.fff}  [{category,-11}] {message}{Environment.NewLine}");

    private void Append(string text)
    {
        lock (_sync)
        {
            try
            {
                File.AppendAllText(Path, text, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Diagnostics must never break the thing they are diagnosing.
            }
        }
    }

    private void RollIfTooLarge()
    {
        try
        {
            if (!File.Exists(Path) || new FileInfo(Path).Length < MaxBytes) return;
            File.Move(Path, Path + ".old", overwrite: true);
        }
        catch (Exception)
        {
            // Not worth failing startup over.
        }
    }
}
