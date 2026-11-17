using System.Net;

namespace AqwCompanion.App.Updates;

/// <summary>The HTTP clients of the update code: no redirects (the code validates each hop), no cookies, no credentials. Every call has its own deadline.</summary>
public static class UpdateHttp
{
    public static SocketsHttpHandler CreateApiHandler() => Handler(DecompressionMethods.All);

    /// <summary>No decompression: the content length of a download is the size of the file that is hashed.</summary>
    public static SocketsHttpHandler CreateDownloadHandler() => Handler(DecompressionMethods.None);

    public static HttpClient CreateApi() => new(CreateApiHandler()) { Timeout = Timeout.InfiniteTimeSpan };

    public static HttpClient CreateDownload() => new(CreateDownloadHandler()) { Timeout = Timeout.InfiniteTimeSpan };

    private static SocketsHttpHandler Handler(DecompressionMethods decompression) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = true,
        Credentials = null,
        PreAuthenticate = false,
        AutomaticDecompression = decompression,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };
}
