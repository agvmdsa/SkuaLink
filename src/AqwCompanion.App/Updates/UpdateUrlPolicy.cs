namespace AqwCompanion.App.Updates;

/// <summary>Where SkuaLink may talk to: the GitHub API for the release data, GitHub and its asset host for downloads. Applied to every hop.</summary>
public static class UpdateUrlPolicy
{
    public const int MaxRedirects = 5;

    private const string AssetHostSuffix = ".githubusercontent.com";

    public static bool IsAllowedDownload(Uri? uri)
    {
        if (!IsPlainHttps(uri))
        {
            return false;
        }

        var host = uri!.IdnHost.ToLowerInvariant();
        return host == "github.com" || (host.Length > AssetHostSuffix.Length && host.EndsWith(AssetHostSuffix, StringComparison.Ordinal));
    }

    public static bool IsAllowedApi(Uri? uri) => IsPlainHttps(uri) && uri!.IdnHost.Equals("api.github.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlainHttps(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && uri.UserInfo.Length == 0;
}
