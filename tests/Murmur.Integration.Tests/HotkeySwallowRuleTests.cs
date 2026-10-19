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

    // Window messages cannot say which Ctrl was pressed, so the rebinding dialog probes the
    // physical key state. Without this, choosing Right Ctrl silently binds both sides.

    [Theory]
    [InlineData(Keys.ControlKey, Keys.RControlKey)]
    [InlineData(Keys.ShiftKey, Keys.RShiftKey)]
    [InlineData(Keys.Menu, Keys.RMenu)]
    public void The_right_hand_side_is_resolved_when_it_is_down(Keys generic, Keys expected)
    {
        Assert.Equal(expected, HotkeySwallowRule.ResolveSide(generic, key => key == expected));
    }

    [Theory]
    [InlineData(Keys.ControlKey, Keys.LControlKey)]
    [InlineData(Keys.ShiftKey, Keys.LShiftKey)]
    [InlineData(Keys.Menu, Keys.LMenu)]
    public void The_left_hand_side_is_resolved_when_it_is_down(Keys generic, Keys expected)
    {
        Assert.Equal(expected, HotkeySwallowRule.ResolveSide(generic, key => key == expected));
    }

    [Fact]
    public void An_unresolvable_modifier_stays_generic_and_matches_either_side()
    {
        var resolved = HotkeySwallowRule.ResolveSide(Keys.ControlKey, _ => false);

        Assert.Equal(Keys.ControlKey, resolved);
        Assert.True(HotkeySwallowRule.Matches(resolved, Keys.LControlKey));
        Assert.True(HotkeySwallowRule.Matches(resolved, Keys.RControlKey));
    }

    [Theory]
    [InlineData(Keys.Oem6)]
    [InlineData(Keys.F9)]
    [InlineData(Keys.RControlKey)]
    public void Non_generic_keys_are_returned_unchanged(Keys key)
    {
        Assert.Equal(key, HotkeySwallowRule.ResolveSide(key, _ => true));
    }
}
