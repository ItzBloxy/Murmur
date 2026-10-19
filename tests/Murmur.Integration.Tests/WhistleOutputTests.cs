using Murmur.Platform;

namespace Murmur.Integration.Tests;

public sealed class WhistleOutputTests
{
    [Fact]
    public void Reads_the_transcript_and_timings()
    {
        var result = WhistleOutput.Parse(
            """{"text":"Please send the report.","language":"en","ttft_ms":22.9,"decode_tps":730.6}""");

        Assert.Equal("Please send the report.", result.Text);
        Assert.Equal("en", result.Language);
        Assert.Equal(22.9, result.TtftMs);
        Assert.Equal(730.6, result.DecodeTps);
    }

    [Fact]
    public void Decodes_escaped_non_ascii()
    {
        var result = WhistleOutput.Parse("""{"text":"Grüße aus München","language":"de"}""");

        Assert.Equal("Grüße aus München", result.Text);
    }

    [Fact]
    public void Keeps_raw_non_ascii()
    {
        var result = WhistleOutput.Parse("""{"text":"Zażółć gęślą jaźń","language":"pl"}""");

        Assert.Equal("Zażółć gęślą jaźń", result.Text);
    }

    [Fact]
    public void Finds_the_json_among_other_output()
    {
        var result = WhistleOutput.Parse(
            "loading whistle.cact\r\n" +
            """{"text":" Bonjour à tous. ","language":"fr","ttft_ms":1,"decode_tps":2}""" + "\r\n");

        Assert.Equal("Bonjour à tous.", result.Text);
    }

    /// <summary>needle.h: silence and steady noise give an empty text and language.</summary>
    [Fact]
    public void Silence_is_empty_text()
    {
        var result = WhistleOutput.Parse("""{"text":"","language":"","ttft_ms":0.0,"decode_tps":0.0}""");

        Assert.Equal("", result.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("error: could not open model")]
    [InlineData("{\"language\":\"de\"}")]
    public void Output_without_a_transcript_is_an_error(string stdout)
    {
        Assert.Throws<InvalidDataException>(() => WhistleOutput.Parse(stdout));
    }
}
