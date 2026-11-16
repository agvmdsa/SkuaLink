using System.Buffers.Text;
using System.Security.Cryptography;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Push;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Push;

public class PushNotifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class Keys(VapidKey key) : IVapidKeySource
    {
        public VapidKey GetOrCreate() => key;
    }

    private readonly FakePushHandler _handler = new();
    private readonly PushSubscriptionStore _store = new(new MemoryPushFileStore(), () => Now);

    private void Subscribe(string device, string[] kinds)
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = ua.ExportParameters(false).Q;
        _store.Set(
            device, $"https://fcm.googleapis.com/fcm/send/{device}", Base64Url.EncodeToString([0x04, .. q.X!, .. q.Y!]),
            Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)), kinds);
    }

    private PushDispatcher Dispatcher() =>
        new(new PushSender(new HttpClient(_handler), new Keys(VapidKey.Create()), () => Now, (_, _) => Task.CompletedTask), _store);

    private static Notice N(NoticeKind kind) => new("n1", kind, "Hero", "Title", "Body", Now);

    [Fact]
    public async Task A_notice_goes_to_each_device_whose_switch_for_its_kind_is_on()
    {
        Subscribe("one", ["RankUp"]);
        Subscribe("two", ["ScriptError"]);
        using var dispatcher = Dispatcher();

        new PushNotifier(_store, dispatcher).Publish(N(NoticeKind.RankUp));
        await dispatcher.StopAsync();

        var seen = Assert.Single(_handler.Requests);
        Assert.EndsWith("/one", seen.Request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_kind_nobody_enabled_sends_nothing()
    {
        Subscribe("one", ["ScriptError"]);
        using var dispatcher = Dispatcher();

        new PushNotifier(_store, dispatcher).Publish(N(NoticeKind.HardFarmDrop));
        await dispatcher.StopAsync();

        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData(NoticeKind.NewLogin)]
    [InlineData(NoticeKind.SkuaReconnected)]
    [InlineData(NoticeKind.ActivityStopped)]
    public async Task A_notice_kind_that_is_never_pushed_sends_nothing(NoticeKind kind)
    {
        Subscribe("one", [.. PushKinds.All]);
        using var dispatcher = Dispatcher();

        new PushNotifier(_store, dispatcher).Publish(N(kind));
        await dispatcher.StopAsync();

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public void A_throwing_store_or_dispatcher_never_throws_into_the_caller()
    {
        Subscribe("one", ["RankUp"]);
        var dispatcher = Dispatcher();
        dispatcher.Dispose();

        new PushNotifier(_store, dispatcher).Publish(N(NoticeKind.RankUp));
        new PushNotifier(null!, dispatcher).Publish(N(NoticeKind.RankUp));
    }
}
