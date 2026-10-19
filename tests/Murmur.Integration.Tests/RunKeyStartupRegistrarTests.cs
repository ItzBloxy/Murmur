using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class RunKeyStartupRegistrarTests : IDisposable
{
    private readonly RunKeyStartupRegistrar _registrar = new("MurmurTest");
    private readonly List<string> _files = [];

    public void Dispose()
    {
        _registrar.Disable();
        foreach (var file in _files) File.Delete(file);
    }

    [Fact]
    public void Starts_disabled()
    {
        _registrar.Disable();

        Assert.False(_registrar.IsEnabled);
    }

    [Fact]
    public void Enable_then_IsEnabled_is_true()
    {
        _registrar.Enable(@"C:\Tools\Murmur.exe");

        Assert.True(_registrar.IsEnabled);
    }

    [Fact]
    public void Disable_removes_the_entry()
    {
        _registrar.Enable(@"C:\Tools\Murmur.exe");

        _registrar.Disable();

        Assert.False(_registrar.IsEnabled);
    }

    [Fact]
    public void Disable_is_safe_when_not_enabled()
    {
        _registrar.Disable();
        _registrar.Disable();

        Assert.False(_registrar.IsEnabled);
    }

    [Fact]
    public void Enable_is_idempotent()
    {
        _registrar.Enable(@"C:\Tools\Murmur.exe");
        _registrar.Enable(@"C:\Tools\Murmur.exe");

        Assert.True(_registrar.IsEnabled);
    }

    [Fact]
    public void Reports_the_registered_path_without_quotes()
    {
        _registrar.Enable(@"C:\Tools\Murmur.exe");

        Assert.Equal(@"C:\Tools\Murmur.exe", _registrar.RegisteredPath);
    }

    /// <summary>A downloaded single-file exe gets moved; autostart must follow it rather than launch nothing.</summary>
    [Fact]
    public void A_registration_pointing_at_a_missing_file_is_moved_to_this_copy()
    {
        _registrar.Enable(@"C:\does-not-exist\Murmur.App.exe");
        var current = ExistingFile();

        Assert.True(_registrar.RepairIfMoved(current));
        Assert.Equal(current, _registrar.RegisteredPath);
    }

    /// <summary>A second copy, such as a dev build, must not take autostart from one that still exists.</summary>
    [Fact]
    public void A_registration_that_still_resolves_is_left_alone()
    {
        var registered = ExistingFile();
        _registrar.Enable(registered);

        Assert.False(_registrar.RepairIfMoved(ExistingFile()));
        Assert.Equal(registered, _registrar.RegisteredPath);
    }

    [Fact]
    public void Repair_never_enables_autostart_that_is_off()
    {
        _registrar.Disable();

        Assert.False(_registrar.RepairIfMoved(ExistingFile()));
        Assert.False(_registrar.IsEnabled);
    }

    private string ExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"murmur-autostart-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, []);
        _files.Add(path);
        return path;
    }
}
