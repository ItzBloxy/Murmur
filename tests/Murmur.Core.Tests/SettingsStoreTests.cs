using Murmur.Core;

namespace Murmur.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "murmur-tests", Guid.NewGuid().ToString("N"));

    private string PathFor(string name) => Path.Combine(_dir, name);

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var store = new SettingsStore(PathFor("settings.json"));

        var settings = store.Load();

        Assert.Equal("da", settings.Language);
        Assert.Equal("RControlKey", settings.Hotkey);
        Assert.Equal(700, settings.HoldThresholdMs);
        Assert.Equal(5, settings.IdleUnloadMinutes);
        Assert.Equal(5, settings.MaxRecordingMinutes);
        Assert.Equal(500, settings.VadSilenceMs);
        Assert.False(settings.StartWithWindows);
        Assert.Equal("auto", settings.Device);
    }

    [Fact]
    public void Save_then_Load_round_trips_every_field()
    {
        var path = PathFor("settings.json");
        var store = new SettingsStore(path);
        var original = new Settings
        {
            Language = "en",
            Hotkey = "F9",
            HoldThresholdMs = 250,
            IdleUnloadMinutes = 2,
            MaxRecordingMinutes = 10,
            VadSilenceMs = 700,
            StartWithWindows = true,
            Device = "cpu",
        };

        store.Save(original);
        var loaded = new SettingsStore(path).Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Save_creates_the_directory_when_it_does_not_exist()
    {
        var path = Path.Combine(_dir, "nested", "deeper", "settings.json");
        var store = new SettingsStore(path);

        store.Save(new Settings { Language = "en" });

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Load_backs_up_corrupt_file_and_returns_defaults()
    {
        var path = PathFor("settings.json");
        File.WriteAllText(path, "{ this is not json");
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal("da", settings.Language);
        Assert.True(File.Exists(path + ".corrupt"), "corrupt file should be preserved for inspection");
    }

    [Fact]
    public void Load_treats_a_json_null_document_as_corrupt()
    {
        var path = PathFor("settings.json");
        File.WriteAllText(path, "null");
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal("da", settings.Language);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void Save_overwrites_an_existing_file()
    {
        var path = PathFor("settings.json");
        var store = new SettingsStore(path);

        store.Save(new Settings { Language = "da" });
        store.Save(new Settings { Language = "en" });

        Assert.Equal("en", store.Load().Language);
    }
}
