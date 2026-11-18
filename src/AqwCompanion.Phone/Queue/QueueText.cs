using System.Globalization;
using System.Text;

namespace AqwCompanion.Phone.Queue;

public static class QueueText
{
    /// <summary>The text without control, format and separator characters and lone surrogates, trimmed, and cut to <paramref name="max"/> without splitting a surrogate pair.</summary>
    public static string Clean(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[i + 1]);
                i++;
            }
            else if (!char.IsSurrogate(c) && !char.IsControl(c) && !IsInvisible(c))
            {
                builder.Append(c);
            }
        }

        var clean = builder.ToString().Trim();
        if (clean.Length <= max)
        {
            return clean;
        }

        var end = max > 0 && char.IsHighSurrogate(clean[max - 1]) ? max - 1 : max;
        return clean[..end].TrimEnd();
    }

    /// <summary>Format characters (bidi controls, zero-width marks) and the line and paragraph separators change how a name renders.</summary>
    private static bool IsInvisible(char c) =>
        char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    /// <summary>Item, faction and script names match when equal after trimming, ignoring case; never when either is null.</summary>
    public static bool SameName(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
