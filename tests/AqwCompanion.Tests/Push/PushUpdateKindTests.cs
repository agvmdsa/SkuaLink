using System.Buffers.Text;
using System.Security.Cryptography;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Push;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Push;

public class PushUpdateKindTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private const string Fcm = "https://fcm.googleapis.com/fcm/send/abc";

    private sealed class Keys(VapidKey key) : IVapidKeySource
    {
        public VapidKey GetOrCreate() => key;

        public VapidKey? GetExisting() => key;
    }

    private static Notice Update() =>
        new("update:0.8.0", NoticeKind.UpdateAvailable, string.Empty, "SkuaLink update available", "SkuaLink 0.8.0 is available. Update it on the PC.", Now);

    [Fact]
    public void The_update_kind_is_known_last_and_on_by_default_for_a_new_subscription()
    {
        Assert.Equal("UpdateAvailable", PushKinds.All[^1]);
        Assert.Equal(10, PushKinds.All.Count);
        var store = new PushSubscriptionStore(new MemoryPushFileStore(), () => Now);

        var sub = store.Set("d1", Fcm, "P", "A", null);

        Assert.Contains("UpdateAvailable", sub.Kinds);
    }

    [Fact]
    public void The_update_payload_opens_the_list_and_is_not_urgent()
    {
        var message = PushPayload.Build(Update());

        using var json = System.Text.Json.JsonDocument.Parse(message.Payload);
        Assert.Equal("/", json.RootElement.GetProperty("url").GetString());
        Assert.Equal("UpdateAvailable", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal(PushUrgency.Normal, message.Urgency);
        Assert.Equal(3600, message.TtlSeconds);
    }

    [Fact]
    public async Task A_phone_with_the_switch_on_gets_the_push_and_one_with_it_off_does_not()
    {
        var store = new PushSubscriptionStore(new MemoryPushFileStore(), () => Now);
        var handler = new FakePushHandler();
        Subscribe(store, "on", ["UpdateAvailable"]);
        Subscribe(store, "off", ["RankUp"]);
        using var dispatcher = new PushDispatcher(
            new PushSender(new HttpClient(handler), new Keys(VapidKey.Create()), () => Now, (_, _) => Task.CompletedTask), store);

        new PushNotifier(store, dispatcher).Publish(Update());
        await dispatcher.StopAsync();

        var seen = Assert.Single(handler.Requests);
        Assert.EndsWith("/on", seen.Request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    private static void Subscribe(PushSubscriptionStore store, string device, string[] kinds)
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = ua.ExportParameters(false).Q;
        store.Set(
            device, $"https://fcm.googleapis.com/fcm/send/{device}", Base64Url.EncodeToString([0x04, .. q.X!, .. q.Y!]),
            Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)), kinds);
    }
}
