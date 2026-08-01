namespace Murmur.Integration.Tests;

/// <summary>
/// Levenshtein distance over words, divided by reference length. Used instead of exact
/// string equality because exact matches break on every model update for no real reason.
/// </summary>
public static class WordErrorRate
{
    public static double Compute(string reference, string hypothesis)
    {
        var refWords = Tokenize(reference);
        var hypWords = Tokenize(hypothesis);

        if (refWords.Length == 0) return hypWords.Length == 0 ? 0 : 1;

        var previous = new int[hypWords.Length + 1];
        var current = new int[hypWords.Length + 1];

        for (var j = 0; j <= hypWords.Length; j++) previous[j] = j;

        for (var i = 1; i <= refWords.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= hypWords.Length; j++)
            {
                var cost = refWords[i - 1] == hypWords[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return (double)previous[hypWords.Length] / refWords.Length;
    }

    private static string[] Tokenize(string text) => text
        .ToLowerInvariant()
        .Split([' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
}
