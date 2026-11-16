using System.Net;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>An <see cref="HttpMessageHandler"/> that answers from a queue and records every request (its body copied).</summary>
public sealed class FakePushHandler : HttpMessageHandler
{
    public sealed record Seen(HttpRequestMessage Request, byte[] Body);

    private readonly Queue<Func<HttpResponseMessage>> _answers = new();

    public List<Seen> Requests { get; } = [];

    public FakePushHandler Answer(HttpStatusCode status, string? body = null, int? retryAfterSeconds = null)
    {
        _answers.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body ?? string.Empty) };
            if (retryAfterSeconds is { } seconds)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            return response;
        });
        return this;
    }

    public FakePushHandler Throw(Exception exception)
    {
        _answers.Enqueue(() => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add(new Seen(request, body));
        return _answers.Count == 0 ? new HttpResponseMessage(HttpStatusCode.Created) : _answers.Dequeue()();
    }
}
