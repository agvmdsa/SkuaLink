using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog;

namespace AqwCompanion.App.Skua;

/// <summary>Opens the release page of SkuaLink in the system browser. Only the addresses <see cref="ReleasePage.IsAllowed"/> accepts are ever launched.</summary>
public interface IReleasePageLauncher
{
    /// <summary>True when the URL is the release page and was handed to the system; false for any other text or a failed start. Never throws.</summary>
    bool Open(string url);
}

public static partial class ReleasePage
{
    public const string Url = "https://github.com/agvmdsa/SkuaLink/releases/latest";

    [GeneratedRegex(@"\Ahttps://github\.com/agvmdsa/SkuaLink/releases/tag/v[0-9]{1,6}\.[0-9]{1,6}\.[0-9]{1,6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagPage();

    /// <summary>The page of one release, built here from a parsed version, never from text that came over the network.</summary>
    public static string ForVersion(AqwCompanion.App.Updates.ReleaseVersion version) =>
        "https://github.com/agvmdsa/SkuaLink/releases/tag/" + version.Tag;

    /// <summary>The latest-release page or the page of one release tag of this repository; nothing else.</summary>
    public static bool IsAllowed(string? url) => url is not null && (string.Equals(url, Url, StringComparison.Ordinal) || TagPage().IsMatch(url));
}

public sealed class ProcessReleasePageLauncher : IReleasePageLauncher
{
    private readonly Func<ProcessStartInfo, bool> _start;

    public ProcessReleasePageLauncher(Func<ProcessStartInfo, bool>? start = null) => _start = start ?? StartProcess;

    public bool Open(string url)
    {
        if (!ReleasePage.IsAllowed(url))
        {
            return false;
        }

        try
        {
            return _start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Opening the release page failed: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    private static bool StartProcess(ProcessStartInfo info) => StartProcess(info, Process.Start);

    /// <summary>
    /// True when the shell took the address. Deliberately not <c>process is not null</c>: with the shell, Process.Start
    /// answers null when no new process was started, which is the usual case for a URL while the browser is already
    /// running (ShellExecuteEx then returns no process handle), so null here is a success. A real failure throws.
    /// </summary>
    internal static bool StartProcess(ProcessStartInfo info, Func<ProcessStartInfo, Process?> start)
    {
        using var process = start(info);
        return true;
    }
}
