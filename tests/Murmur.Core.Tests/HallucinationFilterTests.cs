using Murmur.Core;

namespace Murmur.Core.Tests;

public sealed class HallucinationFilterTests
{
    private readonly HallucinationFilter _filter = new();

    [Theory]
    [InlineData("Thank you for watching!")]
    [InlineData("thanks for watching")]
    [InlineData("  Thank you for watching. ")]
    [InlineData("Tak fordi du så med!")]
    [InlineData("Tak fordi du så med.")]
    [InlineData("Undertekster af Nicolai Winther")]
    [InlineData("Undertekster af Seriefan")]
    [InlineData("Tekstet af Sofie Boysen")]
    [InlineData("Please subscribe")]
    public void Rejects_known_silence_artifacts(string text)
    {
        Assert.True(_filter.ShouldReject(text, noSpeechProbability: 0.1f));
    }

    [Theory]
    [InlineData("Send the report to Anna before Friday.")]
    [InlineData("Jeg kommer hjem klokken syv.")]
    [InlineData("Thanks for watching the deployment logs closely.")]
    public void Keeps_real_speech(string text)
    {
        Assert.False(_filter.ShouldReject(text, noSpeechProbability: 0.1f));
    }

    [Fact]
    public void Rejects_anything_above_the_no_speech_threshold()
    {
        Assert.True(_filter.ShouldReject("Send the report to Anna.", noSpeechProbability: 0.9f));
    }

    [Fact]
    public void Keeps_text_at_exactly_the_threshold()
    {
        Assert.False(_filter.ShouldReject("Send the report.", HallucinationFilter.NoSpeechThreshold));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("...")]
    public void Rejects_empty_and_punctuation_only_text(string text)
    {
        Assert.True(_filter.ShouldReject(text, noSpeechProbability: 0.0f));
    }

    [Fact]
    public void Join_concatenates_surviving_segments_with_single_spaces()
    {
        var segments = new[]
        {
            new TranscribedSegment(" Hello there. ", 0.05f),
            new TranscribedSegment("Thank you for watching", 0.05f),
            new TranscribedSegment("How are you?", 0.05f),
        };

        Assert.Equal("Hello there. How are you?", _filter.Join(segments));
    }

    [Fact]
    public void Join_returns_empty_when_every_segment_is_rejected()
    {
        var segments = new[]
        {
            new TranscribedSegment("Tak fordi du så med", 0.05f),
            new TranscribedSegment("Anything at all", 0.99f),
        };

        Assert.Equal("", _filter.Join(segments));
    }

    [Fact]
    public void Join_handles_an_empty_sequence()
    {
        Assert.Equal("", _filter.Join([]));
    }
}
