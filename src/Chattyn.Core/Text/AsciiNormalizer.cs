using System.Text;

namespace Chattyn.Core.Text;

/// <summary>
/// Prepares text for the WoW chat box: typographic punctuation becomes plain ASCII
/// (curly quotes, dashes, ellipsis), invisible characters are removed and all whitespace,
/// including line breaks, collapses to single spaces. Letters (accents, umlauts) are kept.
/// </summary>
public static class AsciiNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '‘' or '’' or '‚' or '‛' or '′' or '´' or '`':
                    sb.Append('\'');
                    break;
                case '“' or '”' or '„' or '‟' or '″' or '«' or '»':
                    sb.Append('"');
                    break;
                case '–' or '—' or '―' or '−':
                    // En/em dash between words reads as " - " in chat.
                    sb.Append(" - ");
                    break;
                case '‐' or '‑':
                    sb.Append('-');
                    break;
                case '…':
                    sb.Append("...");
                    break;
                case '​' or '‌' or '‍' or '⁠' or '﻿' or '­':
                    break; // zero-width characters and soft hyphen
                default:
                    sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
                    break;
            }
        }

        return CollapseSpaces(sb.ToString());
    }

    private static string CollapseSpaces(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var c in text)
        {
            if (c == ' ')
            {
                if (!lastWasSpace) sb.Append(c);
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }
}
