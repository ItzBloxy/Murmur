using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class RunKeyStartupRegistrarTests : IDisposable
{
    private readonly RunKeyStartupRegistrar _registrar = new("MurmurTest");

    public void Dispose() => _registrar.Disable();

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
}
