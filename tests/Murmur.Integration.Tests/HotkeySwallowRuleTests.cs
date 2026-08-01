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

    // The rebinding dialog reads virtual-key codes from window messages, which report the
    // generic VK_CONTROL / VK_SHIFT / VK_MENU with no side information. WH_KEYBOARD_LL
    // always reports the side-specific code. Without this mapping a modifier bound in the
    // dialog can never match a hook event, and the hotkey silently does nothing.

    [Theory]
    [InlineData(Keys.ControlKey, Keys.LControlKey)]
    [InlineData(Keys.ControlKey, Keys.RControlKey)]
    [InlineData(Keys.ShiftKey, Keys.LShiftKey)]
    [InlineData(Keys.ShiftKey, Keys.RShiftKey)]
    [InlineData(Keys.Menu, Keys.LMenu)]
    [InlineData(Keys.Menu, Keys.RMenu)]
    public void A_generic_modifier_binding_matches_either_side(Keys bound, Keys reported)
    {
        Assert.True(HotkeySwallowRule.Matches(bound, reported));
    }

    [Theory]
    [InlineData(Keys.RControlKey, Keys.LControlKey)]
    [InlineData(Keys.LControlKey, Keys.RControlKey)]
    [InlineData(Keys.RMenu, Keys.LMenu)]
    public void A_side_specific_binding_matches_only_that_side(Keys bound, Keys reported)
    {
        Assert.False(HotkeySwallowRule.Matches(bound, reported));
    }

    [Fact]
    public void A_binding_matches_itself()
    {
        Assert.True(HotkeySwallowRule.Matches(Keys.Oem6, Keys.Oem6));
        Assert.True(HotkeySwallowRule.Matches(Keys.RControlKey, Keys.RControlKey));
    }

    [Fact]
    public void Unrelated_keys_do_not_match()
    {
        Assert.False(HotkeySwallowRule.Matches(Keys.Oem6, Keys.F9));
        Assert.False(HotkeySwallowRule.Matches(Keys.ControlKey, Keys.LMenu));
    }

    [Theory]
    [InlineData(Keys.ControlKey)]
    [InlineData(Keys.ShiftKey)]
    [InlineData(Keys.Menu)]
    public void Generic_modifier_bindings_also_pass_through(Keys key)
    {
        // Otherwise binding "Ctrl" would swallow every Ctrl press and break Ctrl+C.
        Assert.False(HotkeySwallowRule.ShouldSwallow(key));
    }
}
