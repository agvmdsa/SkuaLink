using System.Globalization;
using System.Text.RegularExpressions;

namespace AqwCompanion.App.Updates;

/// <summary>A release number <c>major.minor.patch</c>, compared numerically. A pre-release suffix is never part of it.</summary>
public readonly partial record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    // ASCII digits only ([0-9], never \d: it matches other scripts) and \z (never $: it matches before a final newline).
    [GeneratedRegex(@"\Av([0-9]{1,6})\.([0-9]{1,6})\.([0-9]{1,6})\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\A([0-9]{1,6})\.([0-9]{1,6})\.([0-9]{1,6})(-[0-9A-Za-z.-]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex AppPattern();

    public string Tag => "v" + ToString();

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    public int CompareTo(ReleaseVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    /// <summary>A release tag exactly of the shape <c>v1.2.3</c>.</summary>
    public static bool TryParseTag(string? tag, out ReleaseVersion version)
    {
        version = default;
        if (tag is null || TagPattern().Match(tag) is not { Success: true } m)
        {
            return false;
        }

        version = From(m);
        return true;
    }

    /// <summary>The app's informational version: <c>1.2.3</c>, <c>1.2.3-dev.4</c>, either with <c>+build</c> metadata that is dropped first.</summary>
    public static bool TryParseApp(string? text, out ReleaseVersion version, out bool isPreRelease)
    {
        version = default;
        isPreRelease = false;
        if (text is null)
        {
            return false;
        }

        var plus = text.IndexOf('+', StringComparison.Ordinal);
        var bare = plus >= 0 ? text[..plus] : text;
        if (AppPattern().Match(bare) is not { Success: true } m)
        {
            return false;
        }

        version = From(m);
        isPreRelease = m.Groups[4].Success;
        return true;
    }

    private static ReleaseVersion From(Match m) => new(
        int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
        int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
        int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
}
