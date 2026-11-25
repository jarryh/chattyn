using System.Text;
using System.Text.RegularExpressions;

namespace Chattyn.Core.Text;

/// <summary>
/// Splits a post into WoW chat messages that each fit the byte limit (255 by default).
/// Splits prefer sentence ends, then spaces, and never cut a word unless it alone is too long.
/// A leading chat command ("/e ", "/s ", "/w Name ", "/1 ") is repeated on every part so each
/// part goes to the same channel.
/// </summary>
public static partial class ChatChunker
{
    private const int MinBodyBytes = 16;

    public static IReadOnlyList<string> Split(string? text, int maxBytes = 255, bool numberParts = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 32);

        text = text?.Trim() ?? "";
        if (text.Length == 0) return [];
        if (Bytes(text) <= maxBytes) return [text];

        var (prefix, body) = SplitCommandPrefix(text);
        var estimate = 2;
        while (true)
        {
            var suffixBytes = numberParts ? Bytes($" ({estimate}/{estimate})") : 0;
            var budget = maxBytes - Bytes(prefix) - suffixBytes;
            if (budget < MinBodyBytes && prefix.Length > 0)
            {
                // Absurdly long command prefix: send it once as part of the text instead.
                (prefix, body) = ("", text);
                continue;
            }

            var pieces = SplitBody(body, budget);
            if (!numberParts || Digits(pieces.Count) <= Digits(estimate))
            {
                return pieces
                    .Select((piece, i) => prefix + piece + (numberParts ? $" ({i + 1}/{pieces.Count})" : ""))
                    .ToList();
            }
            estimate = pieces.Count;
        }
    }

    /// <summary>UTF-8 byte count, which is what WoW's 255 limit measures.</summary>
    public static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    /// <summary>Separates a leading chat command such as "/e " or "/w Name " from the text.</summary>
    public static (string Prefix, string Body) SplitCommandPrefix(string text)
    {
        var match = CommandPrefix().Match(text);
        if (!match.Success || match.Length >= text.Length) return ("", text);
        return (match.Value.TrimEnd() + " ", text[match.Length..]);
    }

    private static List<string> SplitBody(string body, int budget)
    {
        var parts = new List<string>();
        var rest = body.Trim();
        while (Bytes(rest) > budget)
        {
            var cut = MaxCharsWithinBytes(rest, budget);
            var breakAt = FindSentenceBreak(rest, cut, minIndex: cut / 2)
                          ?? FindSpaceBreak(rest, cut)
                          ?? cut;
            parts.Add(rest[..breakAt].TrimEnd());
            rest = rest[breakAt..].TrimStart();
        }
        if (rest.Length > 0) parts.Add(rest);
        return parts;
    }

    /// <summary>Number of leading chars whose UTF-8 size fits the budget, never splitting a surrogate pair.</summary>
    private static int MaxCharsWithinBytes(string text, int budget)
    {
        int i = 0, used = 0;
        while (i < text.Length)
        {
            int chars, bytes;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                (chars, bytes) = (2, 4);
            else
                (chars, bytes) = (1, Encoding.UTF8.GetByteCount(text.AsSpan(i, 1)));

            if (used + bytes > budget) break;
            used += bytes;
            i += chars;
        }
        return Math.Max(i, 1);
    }

    /// <summary>Index of a space that follows a sentence end (. ! ? optionally followed by a quote or bracket).</summary>
    private static int? FindSentenceBreak(string text, int maxIndex, int minIndex)
    {
        for (var i = Math.Min(maxIndex, text.Length - 1); i > minIndex && i > 0; i--)
        {
            if (text[i] != ' ') continue;
            var j = i - 1;
            while (j > 0 && text[j] is '"' or '\'' or ')') j--;
            if (text[j] is '.' or '!' or '?') return i;
        }
        return null;
    }

    private static int? FindSpaceBreak(string text, int maxIndex)
    {
        for (var i = Math.Min(maxIndex, text.Length - 1); i > 0; i--)
        {
            if (text[i] == ' ') return i;
        }
        return null;
    }

    private static int Digits(int n) => n.ToString().Length;

    [GeneratedRegex(@"^/(?:(?:e|em|emote|me|s|say|y|yell|p|party|g|guild|o|officer|ra|raid|rw|i|instance|bg)\s+|(?:w|whisper|t|tell)\s+\S+\s+|\d+\s+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommandPrefix();
}
