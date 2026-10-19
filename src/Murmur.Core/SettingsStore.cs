using System.Text.Json;

namespace Murmur.Core;

/// <summary>
/// Loads and saves <see cref="Settings"/> as JSON. A corrupt file is moved aside rather
/// than thrown at the user — configuration must never block startup.
/// </summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Path => path;

    public Settings Load()
    {
        if (!File.Exists(path)) return new Settings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Settings>(json, Options) ?? throw new JsonException("null document");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            TryBackup();
            return new Settings();
        }
    }

    public void Save(Settings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, overwrite: true);
    }

    private void TryBackup()
    {
        try { File.Move(path, path + ".corrupt", overwrite: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
