using Murmur.Core;

namespace Murmur.Core.Tests;

public sealed class SpeechLanguagesTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("da")]
    public void English_and_danish_stay_on_whisper(string code)
    {
        Assert.Equal(SpeechModel.Whisper, SpeechLanguages.ModelFor(code));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("it")]
    [InlineData("nl")]
    [InlineData("pl")]
    public void The_six_new_languages_run_on_whistle(string code)
    {
        Assert.Equal(SpeechModel.Whistle, SpeechLanguages.ModelFor(code));
    }

    [Fact]
    public void Lookup_ignores_case()
    {
        Assert.Equal(SpeechModel.Whistle, SpeechLanguages.ModelFor("DE"));
    }

    /// <summary>
    /// A hand-edited settings file may name a language the menu does not offer. Whisper
    /// understands nearly every language, Whistle only seven, so Whisper is the safe home.
    /// </summary>
    [Theory]
    [InlineData("sv")]
    [InlineData("")]
    public void An_unknown_language_falls_back_to_whisper(string code)
    {
        Assert.Equal(SpeechModel.Whisper, SpeechLanguages.ModelFor(code));
    }

    [Fact]
    public void The_menu_lists_whisper_then_whistle_in_the_requested_order()
    {
        Assert.Equal(
            ["English", "Danish", "German", "French", "Spanish", "Italian", "Dutch", "Polish"],
            SpeechLanguages.All.Select(l => l.DisplayName));

        Assert.Equal(
            [SpeechModel.Whisper, SpeechModel.Whisper, .. Enumerable.Repeat(SpeechModel.Whistle, 6)],
            SpeechLanguages.All.Select(l => l.Model));
    }

    [Fact]
    public void Codes_are_unique()
    {
        Assert.Equal(SpeechLanguages.All.Count, SpeechLanguages.All.Select(l => l.Code).Distinct().Count());
    }
}
