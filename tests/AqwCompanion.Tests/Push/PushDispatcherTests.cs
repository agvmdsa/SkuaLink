using System.Buffers.Text;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using AqwCompanion.Phone.Push;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Push;

public class PushDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class Keys(VapidKey key) : IVapidKeySource
    {
        public VapidKey GetOrCreate() => key;

        public VapidKey? GetExisting() => key;
    }

    /// <summary>Records the TTL header of every request; the first request waits for <see cref="Gate"/>.</summary>
    private sealed class GateHandler : HttpMessageHandler
    {
        private readonly object _lock = new();
        public List<string> Ttls { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool first;
            lock (_lock)
            {
                Ttls.Add(request.Headers.GetValues("TTL").Single());
                first = Ttls.Count == 1;
            }

            if (first)
            {
                Started.TrySetResult();
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }

    private readonly MemoryPushFileStore _file = new();
    private readonly List<Exception> _errors = [];
    private readonly PushSubscriptionStore _store;

    public PushDispatcherTests()
    {
        _store = new PushSubscriptionStore(_file, () => Now);
        var keys = NewKeys();
        _store.Set("dev", "https://fcm.googleapis.com/fcm/send/abc", keys.P256dh, keys.Auth, null);
    }

    private static (string P256dh, string Auth) NewKeys()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = ua.ExportParameters(false).Q;
        return (Base64Url.EncodeToString([0x04, .. q.X!, .. q.Y!]), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
    }

    private PushDispatcher Dispatcher(HttpMessageHandler handler) =>
        new(new PushSender(new HttpClient(handler), new Keys(VapidKey.Create()), () => Now, (_, _) => Task.CompletedTask), _store, _errors.Add);

    private static PushJob Job(string device = "dev", int ttl = 3600) => new(device, new PushMessage("{\"title\":\"t\"}"u8.ToArray(), ttl, PushUrgency.Normal));

    [Fact]
    public async Task A_sent_job_records_the_success()
    {
        var handler = new FakePushHandler().Answer(HttpStatusCode.Created);
        using var dispatcher = Dispatcher(handler);

        Assert.True(dispatcher.Enqueue(Job()));
        await dispatcher.StopAsync();

        Assert.Single(handler.Requests);
        Assert.Equal(Now, _store.Get("dev")!.LastSuccessUtc);
    }

    [Fact]
    public async Task A_410_removes_the_subscription()
    {
        var handler = new FakePushHandler().Answer(HttpStatusCode.Gone);
        using var dispatcher = Dispatcher(handler);

        dispatcher.Enqueue(Job());
        await dispatcher.StopAsync();

        Assert.Null(_store.Get("dev"));
    }

    private sealed class ResubscribeHandler(PushSubscriptionStore store, string device, string newEndpoint, (string P256dh, string Auth) keys) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The phone subscribes again with a new endpoint while the send to the old one is on its way.
            store.Set(device, newEndpoint, keys.P256dh, keys.Auth, null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone));
        }
    }

    [Fact]
    public async Task A_410_for_the_old_endpoint_does_not_remove_a_subscription_made_meanwhile_with_a_new_endpoint()
    {
        const string newEndpoint = "https://updates.push.services.mozilla.com/wpush/v2/new";
        using var dispatcher = Dispatcher(new ResubscribeHandler(_store, "dev", newEndpoint, NewKeys()));

        dispatcher.Enqueue(Job());
        await dispatcher.StopAsync();

        Assert.Equal(newEndpoint, _store.Get("dev")!.Endpoint);
    }

    [Fact]
    public async Task Disposing_after_the_worker_ran_is_safe_twice()
    {
        var dispatcher = Dispatcher(new FakePushHandler());
        dispatcher.Enqueue(Job());
        await dispatcher.StopAsync();

        dispatcher.Dispose();
        dispatcher.Dispose();

        Assert.False(dispatcher.Enqueue(Job()));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_403_records_the_error_and_keeps_the_subscription(HttpStatusCode status)
    {
        var handler = new FakePushHandler().Answer(status);
        using var dispatcher = Dispatcher(handler);

        dispatcher.Enqueue(Job());
        await dispatcher.StopAsync();

        Assert.NotNull(_store.Get("dev"));
        Assert.Equal("The push service refused the key; subscribe again", _store.LastError("dev"));
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Another_failure_records_the_error_without_the_endpoint_and_keeps_the_subscription()
    {
        var handler = new FakePushHandler().Answer(HttpStatusCode.BadRequest);
        using var dispatcher = Dispatcher(handler);

        dispatcher.Enqueue(Job());
        await dispatcher.StopAsync();

        Assert.NotNull(_store.Get("dev"));
        var error = _store.LastError("dev");
        Assert.NotNull(error);
        Assert.DoesNotContain("fcm.googleapis.com", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_job_for_an_unknown_device_sends_nothing()
    {
        var handler = new FakePushHandler();
        using var dispatcher = Dispatcher(handler);

        dispatcher.Enqueue(Job("nobody"));
        await dispatcher.StopAsync();

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_over_cap_payload_is_not_sent_and_a_bad_ttl_is_clamped()
    {
        var handler = new FakePushHandler();
        using var dispatcher = Dispatcher(handler);

        dispatcher.Enqueue(new PushJob("dev", new PushMessage(new byte[2001], 3600, PushUrgency.Normal)));
        dispatcher.Enqueue(Job(ttl: 5));
        dispatcher.Enqueue(Job(ttl: 999999));
        await dispatcher.StopAsync();

        Assert.Equal(["60", "86400"], handler.Requests.Select(r => r.Request.Headers.GetValues("TTL").Single()));
    }

    [Fact]
    public async Task The_queue_is_bounded_and_drops_the_oldest()
    {
        var handler = new GateHandler();
        using var dispatcher = Dispatcher(handler);
        dispatcher.Enqueue(Job(ttl: 100));
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        for (var i = 0; i < 64 + 5; i++)
        {
            dispatcher.Enqueue(Job(ttl: 200 + i));
        }

        handler.Gate.SetResult();
        await dispatcher.StopAsync();

        Assert.InRange(handler.Ttls.Count, 1, 1 + PushDispatcher.Capacity);
        Assert.Equal((200 + 64 + 4).ToString(), handler.Ttls[^1]);
        Assert.DoesNotContain("200", handler.Ttls);
    }

    [Fact]
    public async Task A_failing_handler_does_not_stop_the_loop()
    {
        var handler = new FakePushHandler().Throw(new InvalidOperationException("boom")).Answer(HttpStatusCode.Created);
        using var dispatcher = Dispatcher(handler);

        dispatcher.Enqueue(Job());
        dispatcher.Enqueue(Job());
        await dispatcher.StopAsync();

        Assert.Equal(2, handler.Requests.Count);
        Assert.NotNull(_store.Get("dev")!.LastSuccessUtc);
    }

    [Fact]
    public void Enqueue_after_Dispose_returns_false_and_does_not_throw()
    {
        var dispatcher = Dispatcher(new FakePushHandler());
        dispatcher.Dispose();

        Assert.False(dispatcher.Enqueue(Job()));
        dispatcher.Dispose();
    }

    [Fact]
    public async Task Enqueue_never_blocks()
    {
        var handler = new GateHandler();
        var dispatcher = Dispatcher(handler);
        try
        {
            dispatcher.Enqueue(Job());
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 1000; i++)
            {
                dispatcher.Enqueue(Job());
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
        }
        finally
        {
            dispatcher.Dispose();
        }
    }

    [Fact]
    public void A_target_and_a_job_never_print_the_endpoint_or_the_keys()
    {
        var target = new PushTarget("https://fcm.googleapis.com/fcm/send/SECRET-ENDPOINT", "SECRET-P256DH", "SECRET-AUTH");
        var job = new PushJob("dev", new PushMessage("{}"u8.ToArray(), 60, PushUrgency.Normal));

        var text = target.ToString() + job.ToString();

        Assert.DoesNotContain("SECRET", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fcm.googleapis.com", text, StringComparison.Ordinal);
        Assert.Contains("<redacted>", target.ToString(), StringComparison.Ordinal);
    }
}
