using Murmur.Core;

namespace Murmur.Core.Tests;

public sealed class FileDiagnosticLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "murmur-log-tests", Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_dir, "murmur.log");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Creates_its_directory_on_construction()
    {
        _ = new FileDiagnosticLog(LogPath);

        Assert.True(Directory.Exists(_dir));
    }

    [Fact]
    public void Session_header_records_every_fact_it_is_given()
    {
        var log = new FileDiagnosticLog(LogPath);

        log.WriteSessionHeader([("version", "1.2.3"), ("hotkey", "RControlKey")]);

        var contents = File.ReadAllText(LogPath);
        Assert.Contains("SESSION START", contents);
        Assert.Contains("version", contents);
        Assert.Contains("1.2.3", contents);
        Assert.Contains("RControlKey", contents);
    }

    [Fact]
    public void Entries_carry_their_category()
    {
        var log = new FileDiagnosticLog(LogPath);

        log.Write("whisper", "backend=Vulkan");

        Assert.Contains("[whisper", File.ReadAllText(LogPath));
    }

    [Fact]
    public void Writing_appends_rather_than_replacing()
    {
        var log = new FileDiagnosticLog(LogPath);

        log.Write("one", "first line");
        log.Write("two", "second line");

        var contents = File.ReadAllText(LogPath);
        Assert.Contains("first line", contents);
        Assert.Contains("second line", contents);
    }

    /// <summary>Diagnostics must never break the thing they are diagnosing.</summary>
    [Fact]
    public void A_locked_file_does_not_throw()
    {
        var log = new FileDiagnosticLog(LogPath);
        log.Write("setup", "creates the file");

        using var holder = File.Open(LogPath, FileMode.Open, FileAccess.Write, FileShare.None);

        log.Write("test", "this cannot be written while the file is locked");
    }
}
