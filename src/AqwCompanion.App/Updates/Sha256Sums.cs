using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AqwCompanion.App.Updates;

public enum SumsLookup { Found, Missing, Malformed }

/// <summary>Reads the <c>SHA256SUMS.txt</c> of a release (<c>&lt;hex&gt;  &lt;file name&gt;</c> per line) and hashes files.</summary>
public static partial class Sha256Sums
{
    [GeneratedRegex(@"\A([0-9A-Fa-f]{64}) [ *](.+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Line();

    /// <summary>
    /// The hash on the line of exactly this file name. Missing: no line names it. Malformed: a line that ends with the name is not a
    /// valid hash line, or two lines give different hashes. The name must match exactly (a path in front of it is another file).
    /// </summary>
    public static SumsLookup TryFind(string text, string fileName, out string hash)
    {
        hash = string.Empty;
        var found = false;
        foreach (var raw in text.TrimStart('\uFEFF').Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var match = Line().Match(line);
            if (match.Success)
            {
                if (match.Groups[2].Value != fileName)
                {
                    continue;
                }

                if (found && !hash.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                {
                    hash = string.Empty;
                    return SumsLookup.Malformed;
                }

                hash = match.Groups[1].Value;
                found = true;
            }
            else if (line.EndsWith(fileName, StringComparison.Ordinal) || NamesFile(line, fileName))
            {
                hash = string.Empty;
                return SumsLookup.Malformed;
            }
        }

        return found ? SumsLookup.Found : SumsLookup.Missing;
    }

    /// <summary>True when a blank-separated word of the line is exactly the file name (trailing text after the name still names it).</summary>
    private static bool NamesFile(string line, string fileName) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => word == fileName || word == "*" + fileName);

    public static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool Matches(string expected, string actual) => string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}
