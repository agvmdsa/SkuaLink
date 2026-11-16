using System.Buffers.Text;
using System.Net;
using System.Text;

namespace AqwCompanion.Phone.Push;

/// <summary>Encrypts and posts one push message. Never throws. The client must not follow redirects (a 3xx is a failure).</summary>
public sealed class PushSender(HttpClient http, IVapidKeySource vapid, Func<DateTimeOffset> now, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(5);
    public const int MaxErrorChars = 120;
    private const int MaxErrorBodyBytes = 512;
    private const string Unreachable = "could not reach the push service";

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    private sealed record Attempt(PushResult Result, TimeSpan? RetryAfter);

    public async Task<PushResult> SendAsync(PushTarget target, PushMessage message, CancellationToken ct)
    {
        try
        {
            if (!PushEndpointPolicy.TryParse(target.Endpoint, out var endpoint))
            {
                return new PushResult(PushOutcome.Failed, null, "endpoint not allowed");
            }

            if (!TryDecode(target, out var p256dh, out var auth))
            {
                return new PushResult(PushOutcome.Failed, null, "invalid subscription keys");
            }

            // The second attempt's own 429/5xx is not retried: its result is returned as it is.
            var attempt = await AttemptAsync(endpoint, p256dh, auth, message, ct).ConfigureAwait(false);
            if (attempt.RetryAfter is { } wait)
            {
                await _delay(wait, ct).ConfigureAwait(false);
                attempt = await AttemptAsync(endpoint, p256dh, auth, message, ct).ConfigureAwait(false);
            }

            return attempt.Result;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new PushResult(PushOutcome.Failed, null, Unreachable);
        }
    }

    /// <summary>One request with a fresh encryption. 2xx Sent; 404/410 Gone; 429 and 5xx Failed with a retry wait; 3xx and other 4xx Failed.</summary>
    private async Task<Attempt> AttemptAsync(Uri endpoint, byte[] p256dh, byte[] auth, PushMessage message, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("Authorization", vapid.GetOrCreate().AuthorizationHeader(endpoint, now()));
        request.Headers.TryAddWithoutValidation("TTL", message.TtlSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Urgency", message.Urgency == PushUrgency.High ? "high" : "normal");
        request.Content = new ByteArrayContent(WebPushEncryption.Encrypt(message.Payload, p256dh, auth));
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is >= 200 and < 300)
            {
                return new Attempt(new PushResult(PushOutcome.Sent, status, null), null);
            }

            if (status is 404 or 410)
            {
                return new Attempt(new PushResult(PushOutcome.Gone, status, null), null);
            }

            if (status is >= 300 and < 400)
            {
                return new Attempt(new PushResult(PushOutcome.Failed, status, "redirect refused"), null);
            }

            var failed = new PushResult(PushOutcome.Failed, status, await ErrorTextAsync(response, status, timeout.Token).ConfigureAwait(false));
            if (status == 429 || status >= 500)
            {
                var seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 1;
                return new Attempt(failed, TimeSpan.FromSeconds(Math.Clamp(seconds, 0, MaxRetryWait.TotalSeconds)));
            }

            return new Attempt(failed, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            return new Attempt(new PushResult(PushOutcome.Failed, null, Unreachable), null);
        }
    }

    private static async Task<string> ErrorTextAsync(HttpResponseMessage response, int status, CancellationToken ct)
    {
        var head = $"push service answered {status}";
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var buffer = new byte[MaxErrorBodyBytes];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var line = text.Split('\n', 2)[0];
            var clean = new string(line.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (clean.Length > MaxErrorChars)
            {
                var cut = MaxErrorChars;
                if (char.IsHighSurrogate(clean[cut - 1]))
                {
                    cut--;
                }

                clean = clean[..cut];
            }

            return clean.Length == 0 ? head : $"{head}: {clean}";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException or ObjectDisposedException)
        {
            return head;
        }
    }

    private static bool TryDecode(PushTarget target, out byte[] p256dh, out byte[] auth)
    {
        p256dh = [];
        auth = [];
        try
        {
            p256dh = Base64Url.DecodeFromChars(target.P256dh);
            auth = Base64Url.DecodeFromChars(target.Auth);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }

        return auth.Length == WebPushEncryption.AuthSecretBytes && WebPushEncryption.IsValidPublicKey(p256dh);
    }
}
