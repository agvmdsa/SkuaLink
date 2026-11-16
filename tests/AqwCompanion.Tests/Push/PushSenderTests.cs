using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using AqwCompanion.Phone.Push;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Push;

public class PushSenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class Keys(VapidKey key) : IVapidKeySource
    {
        public VapidKey GetOrCreate() => key;
    }

    private readonly List<TimeSpan> _waits = [];
    private readonly FakePushHandler _handler = new();
    private readonly VapidKey _vapid = VapidKey.Create();

    private PushSender Sender() => new(new HttpClient(_handler), new Keys(_vapid), () => Now, (wait, _) =>
    {
        _waits.Add(wait);
        return Task.CompletedTask;
    });

    private static PushTarget Target(string endpoint = "https://fcm.googleapis.com/fcm/send/abc")
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = ua.ExportParameters(false).Q;
        return new PushTarget(endpoint, Base64Url.EncodeToString([0x04, .. q.X!, .. q.Y!]), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
    }

    private static PushMessage Message(PushUrgency urgency = PushUrgency.Normal, int ttl = 3600) => new("{\"title\":\"t\"}"u8.ToArray(), ttl, urgency);

    [Fact]
    public async Task A_201_is_sent_and_the_request_carries_the_vapid_ttl_urgency_and_encoding_headers()
    {
        _handler.Answer(HttpStatusCode.Created);

        var result = await Sender().SendAsync(Target(), Message(PushUrgency.High, 300), CancellationToken.None);

        Assert.Equal(new PushResult(PushOutcome.Sent, 201, null), result);
        var seen = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, seen.Request.Method);
        Assert.Equal("https://fcm.googleapis.com/fcm/send/abc", seen.Request.RequestUri!.ToString());
        Assert.StartsWith("vapid t=", seen.Request.Headers.GetValues("Authorization").Single(), StringComparison.Ordinal);
        Assert.EndsWith(", k=" + _vapid.PublicKeyText, seen.Request.Headers.GetValues("Authorization").Single(), StringComparison.Ordinal);
        Assert.Equal("300", seen.Request.Headers.GetValues("TTL").Single());
        Assert.Equal("high", seen.Request.Headers.GetValues("Urgency").Single());
        Assert.Equal("aes128gcm", Assert.Single(seen.Request.Content!.Headers.ContentEncoding));
        Assert.Equal("application/octet-stream", seen.Request.Content.Headers.ContentType!.MediaType);
        Assert.Equal(WebPushEncryption.HeaderBytes + 256 + WebPushEncryption.TagBytes, seen.Body.Length);
    }

    [Fact]
    public async Task Normal_urgency_is_sent_as_normal()
    {
        await Sender().SendAsync(Target(), Message(), CancellationToken.None);

        Assert.Equal("normal", _handler.Requests[0].Request.Headers.GetValues("Urgency").Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task A_404_or_410_means_the_subscription_is_gone(HttpStatusCode status)
    {
        _handler.Answer(status);

        var result = await Sender().SendAsync(Target(), Message(), CancellationToken.None);

        Assert.Equal(PushOutcome.Gone, result.Outcome);
        Assert.Equal((int)status, result.Status);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task A_429_waits_the_retry_after_capped_at_5_seconds_and_retries_once()
    {
        _handler.Answer((HttpStatusCode)429, retryAfterSeconds: 60).Answer(HttpStatusCode.Created);

        var result = await Sender().SendAsync(Target(), Message(), CancellationToken.None);

        Assert.Equal(PushOutcome.Sent, result.Outcome);
        Assert.Equal(2, _handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(5)], _waits);
        Assert.NotEqual(_handler.Requests[0].Body, _handler.Requests[1].Body);   // a fresh encryption per attempt
    }

    [Fact]
    public async Task A_5xx_without_retry_after_waits_one_second_and_a_second_failure_is_reported_with_the_status()
    {
        _handler.Answer(HttpStatusCode.BadGateway).Answer(HttpStatusCode.ServiceUnavailable, "try later");

        var result = await Sender().SendAsync(Target(), Message(), CancellationToken.None);

        Assert.Equal(PushOutcome.Failed, result.Outcome);
        Assert.Equal(503, result.Status);
        Assert.Equal("push service answered 503: try later", result.Error);
        Assert.Equal([TimeSpan.FromSeconds(1)], _waits);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task Another_4xx_is_a_failure_that_keeps_the_subscription_and_is_not_retried()
    {
        _handler.Answer(HttpStatusCode.Forbidden, "bad\r\nvapid\u0007 token " + new string('x', 300));

        var result = await Sender().SendAsync(Target(), Message(), CancellationToken.None);

        Assert.Equal(PushOutcome.Failed, result.Outcome);
        Assert.Equal(403, result.Status);
        Assert.StartsWith("push service answered 403: bad", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', result.Error!);
        Assert.DoesNotContain('\u0007', result.Error!);
        Assert.True(result.Error!.Length <= "push service answered 403: ".Length + PushSender.MaxErrorChars);
        Assert.Single(_handler.Requests);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task A_redirect_is_refused_and_not_followed()
    {
        _handler.Answer(HttpStatusCode.TemporaryRedirect);

        var result = await Sender().SendAsync(Target(), Message(), CancellationToken.None);

        Assert.Equal(new PushResult(PushOutcome.Failed, 307, "redirect refused"), result);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task A_network_failure_is_a_failure_and_never_throws()
    {
        _handler.Throw(new HttpRequestException("no route"));

        var result = await Sender().SendAsync(Target(), Message(), CancellationToken.None);

        Assert.Equal(new PushResult(PushOutcome.Failed, null, "could not reach the push service"), result);
    }

    [Fact]
    public async Task A_timeout_is_a_failure_and_never_throws()
    {
        _handler.Throw(new TaskCanceledException("timed out"));

        var result = await Sender().SendAsync(Target(), Message(), CancellationToken.None);

        Assert.Equal(PushOutcome.Failed, result.Outcome);
        Assert.Equal("could not reach the push service", result.Error);
    }

    [Fact]
    public async Task A_cancelled_send_reports_cancellation_as_a_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _handler.Throw(new OperationCanceledException(cts.Token));

        var result = await Sender().SendAsync(Target(), Message(), cts.Token);

        Assert.Equal(PushOutcome.Failed, result.Outcome);
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("http://fcm.googleapis.com/x")]
    public async Task A_target_outside_the_allow_list_is_failed_without_any_request(string endpoint)
    {
        var result = await Sender().SendAsync(Target(endpoint), Message(), CancellationToken.None);

        Assert.Equal(new PushResult(PushOutcome.Failed, null, "endpoint not allowed"), result);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("AAAA", "AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("not base64!!", "AAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Keys_that_do_not_decode_are_failed_without_any_request(string p256dh, string auth)
    {
        var result = await Sender().SendAsync(new PushTarget("https://fcm.googleapis.com/x", p256dh, auth), Message(), CancellationToken.None);

        Assert.Equal(new PushResult(PushOutcome.Failed, null, "invalid subscription keys"), result);
        Assert.Empty(_handler.Requests);
    }
}
