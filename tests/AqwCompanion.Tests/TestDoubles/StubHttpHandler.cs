using System.Net;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>An <see cref="HttpMessageHandler"/> answered by a function; remembers what was asked.</summary>
public sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public sealed record Seen(Uri Uri, string? IfNoneMatch, string? UserAgent, string? Accept);

    private readonly List<Seen> _requests = [];

    public IReadOnlyList<Seen> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? Header(string name) => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
        lock (_requests)
        {
            _requests.Add(new Seen(request.RequestUri!, Header("If-None-Match"), Header("User-Agent"), Header("Accept")));
        }

        return respond(request, cancellationToken);
    }

    public static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    public static HttpResponseMessage Redirect(string location, HttpStatusCode code = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(code);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    /// <summary>A 200 with these bytes. <paramref name="declaredLength"/> lies about the length; <paramref name="unsized"/> declares none.</summary>
    public static HttpResponseMessage Bytes(byte[] bytes, long? declaredLength = null, bool unsized = false) =>
        new(HttpStatusCode.OK) { Content = new StubContent(bytes, unsized ? null : declaredLength ?? bytes.Length) };

    private sealed class StubContent(byte[] bytes, long? length) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long computed)
        {
            computed = length ?? 0;
            return length is not null;
        }
    }
}
