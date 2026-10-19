using System.Text.Json;

namespace Murmur.Platform;

/// <summary>
/// What needle.exe prints for one transcription — a single JSON line, per needle.h:
/// <c>{"text":"...","language":"en","ttft_ms":0.0,"decode_tps":0.0}</c>.
/// Silence and steady noise give an empty text.
/// </summary>
public sealed record WhistleOutput(string Text, string Language, double TtftMs, double DecodeTps)
{
    public static WhistleOutput Parse(string stdout)
    {
        // The JSON is the last line that looks like JSON; anything before it is the engine talking.
        var line = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.StartsWith('{'));

        if (line is null)
        {
            throw new InvalidDataException($"Whistle printed no transcript: \"{Abbreviate(stdout)}\"");
        }

        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;

            if (!root.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"Whistle's output has no text: \"{Abbreviate(line)}\"");
            }

            return new WhistleOutput(
                text.GetString()!.Trim(),
                root.TryGetProperty("language", out var language) ? language.GetString() ?? "" : "",
                root.TryGetProperty("ttft_ms", out var ttft) ? ttft.GetDouble() : 0,
                root.TryGetProperty("decode_tps", out var tps) ? tps.GetDouble() : 0);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Whistle printed malformed JSON: \"{Abbreviate(line)}\"", ex);
        }
    }

    private static string Abbreviate(string text) => text.Length > 200 ? text[..200] + "…" : text.Trim();
}
