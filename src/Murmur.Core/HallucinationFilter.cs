using System.Text;

namespace Murmur.Core;

/// <summary>
/// Rejects Whisper's silence artifacts. Whisper is trained on subtitle data and reliably
/// emits stock phrases when handed near-silent audio — in a dictation tool that means junk
/// text appearing when the user said nothing.
/// </summary>
public sealed class HallucinationFilter
{
    public const float NoSpeechThreshold = 0.6f;

    /// <summary>Normalized text that is always an artifact, never real dictation.</summary>
    private static readonly HashSet<string> ExactArtifacts = new(StringComparer.Ordinal)
    {
        "thank you for watching",
        "thanks for watching",
        "thank you for watching this video",
        "please subscribe",
        "subscribe",
        "you",
        "bye",
        "tak fordi du sa med",
        "tak for at se med",
        "tak fordi i sa med",
    };

    /// <summary>Prefixes of subtitle credit lines, which carry an arbitrary name after them.</summary>
    private static readonly string[] ArtifactPrefixes =
    [
        "undertekster af",
        "undertekst af",
        "tekstet af",
        "subtitles by",
        "subtitled by",
    ];

    public bool ShouldReject(string text, float noSpeechProbability)
    {
        if (noSpeechProbability > NoSpeechThreshold) return true;

        var normalized = Normalize(text);
        if (normalized.Length == 0) return true;
        if (ExactArtifacts.Contains(normalized)) return true;

        foreach (var prefix in ArtifactPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>Joins the segments that survive filtering into the text to insert.</summary>
    public string Join(IEnumerable<TranscribedSegment> segments)
    {
        var kept = segments
            .Where(s => !ShouldReject(s.Text, s.NoSpeechProbability))
            .Select(s => s.Text.Trim())
            .Where(t => t.Length > 0);

        return string.Join(" ", kept);
    }

    /// <summary>
    /// Lowercases, strips punctuation and collapses whitespace, and folds the Danish
    /// letters å/æ/ø to a/ae/o so blocklist entries stay readable in plain ASCII.
    /// </summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastWasSpace = true;

        foreach (var raw in text)
        {
            var c = char.ToLowerInvariant(raw);
            var mapped = c switch
            {
                'å' => "a",
                'æ' => "ae",
                'ø' => "o",
                _ when char.IsLetterOrDigit(c) => c.ToString(),
                _ when char.IsWhiteSpace(c) => " ",
                _ => "",
            };

            if (mapped.Length == 0) continue;
            if (mapped == " ")
            {
                if (lastWasSpace) continue;
                lastWasSpace = true;
            }
            else
            {
                lastWasSpace = false;
            }

            builder.Append(mapped);
        }

        return builder.ToString().Trim();
    }
}
