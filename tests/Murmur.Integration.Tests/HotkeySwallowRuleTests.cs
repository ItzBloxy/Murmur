using System.Windows.Forms;
using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class HotkeySwallowRuleTests
{
    [Theory]
    [InlineData(Keys.RControlKey)]
    [InlineData(Keys.LControlKey)]
    [InlineData(Keys.RShiftKey)]
    [InlineData(Keys.LShiftKey)]
    [InlineData(Keys.RMenu)]
    [InlineData(Keys.LMenu)]
    [InlineData(Keys.LWin)]
    [InlineData(Keys.RWin)]
    public void Modifier_only_bindings_pass_through(Keys key)
    {
        Assert.False(HotkeySwallowRule.ShouldSwallow(key));
    }

    [Theory]
    [InlineData(Keys.F9)]
    [InlineData(Keys.D)]
    [InlineData(Keys.Space)]
    [InlineData(Keys.Pause)]
    [InlineData(Keys.Scroll)]
    [InlineData(Keys.Insert)]
    public void Normal_keys_are_swallowed(Keys key)
    {
        Assert.True(HotkeySwallowRule.ShouldSwallow(key));
    }

    [Fact]
    public void Parse_accepts_the_default_binding()
    {
        Assert.Equal(Keys.RControlKey, HotkeySwallowRule.Parse("RControlKey"));
    }

    [Fact]
    public void Parse_falls_back_to_the_default_on_nonsense()
    {
        Assert.Equal(Keys.RControlKey, HotkeySwallowRule.Parse("NotAKeyName"));
    }

    [Fact]
    public void Parse_accepts_a_function_key()
    {
        Assert.Equal(Keys.F9, HotkeySwallowRule.Parse("F9"));
    }
}
