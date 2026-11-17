using System.Net;

namespace AqwCompanion.Phone.Push;

/// <summary>
/// The PC posts to a URL a paired phone supplies, so the endpoint must be https on port 443, with no credentials or fragment, on a host
/// of a known push service. Anything else is refused (SSRF).
/// </summary>
public static class PushEndpointPolicy
{
    public const int MaxLength = 1024;

    private static readonly string[] ExactHosts = ["fcm.googleapis.com", "updates.push.services.mozilla.com"];
    private static readonly string[] Suffixes = [".push.apple.com", ".notify.windows.com"];

    public static bool IsAllowed(string? endpoint) => TryParse(endpoint, out _);

    public static bool TryParse(string? endpoint, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrEmpty(endpoint) || endpoint.Length > MaxLength || endpoint.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps || parsed.UserInfo.Length > 0 || parsed.Fragment.Length > 0
            || parsed.Port != 443 || !parsed.IsDefaultPort)
        {
            return false;
        }

        var host = parsed.IdnHost;
        if (host.Length == 0 || host.EndsWith('.') || IPAddress.TryParse(host, out _) || host.Contains(':')
            || !string.Equals(host, host.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return false;
        }

        if (!ExactHosts.Contains(host, StringComparer.Ordinal)
            && !Suffixes.Any(s => host.Length > s.Length && host.EndsWith(s, StringComparison.Ordinal)))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
