namespace AqwCompanion.Phone.Push;

/// <summary>The one HTTP client that posts to push services: no redirects (a 3xx is refused), no cookies, no credentials, a deadline.</summary>
public static class PushHttp
{
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = true,
        Credentials = null,
        PreAuthenticate = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    public static HttpClient Create() => new(CreateHandler()) { Timeout = PushSender.RequestTimeout + TimeSpan.FromSeconds(5) };
}
