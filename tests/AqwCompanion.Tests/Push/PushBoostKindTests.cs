using System.Buffers.Text;
using System.Security.Cryptography;
using AqwCompanion.Phone.Boosts;
using AqwCompanion.Phone.Push;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Push;

public class PushBoostKindTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private const string Fcm = "https://fcm.googleapis.com/fcm/send/abc";

    private sealed class Keys(VapidKey key) : IVapidKeySource
    {
        public VapidKey GetOrCreate() => key;

        public VapidKey? GetExisting() => key;
    }

    private static readonly string[] TheTwelveBefore =
    [
        "ScriptStopped", "ScriptError", "SkuaDisconnected", "CharacterKicked", "SkuaOptionsWaiting", "RankUp",
        "HardFarmDrop", "NoProgress", "DyingOften", "UpdateAvailable", "QueueAttention", "QueueFinished",
    ];

    [Fact]
    public void BoostEnded_is_the_thirteenth_kind_after_QueueFinished()
    {
        Assert.Equal(13, PushKinds.All.Count);
        Assert.Equal("QueueFinished", PushKinds.All[11]);
        Assert.Equal("BoostEnded", PushKinds.All[12]);
    }

    [Fact]
    public void A_new_subscription_has_it_on()
    {
        var store = new PushSubscriptionStore(new MemoryPushFileStore(), () => Now);

        var sub = store.Set("d1", Fcm, "P", "A", null);

        Assert.Contains("BoostEnded", sub.Kinds);
    }

    [Fact]
    public void A_phone_subscribed_before_keeps_its_own_list()
    {
        var file = new MemoryPushFileStore();
        var store = new PushSubscriptionStore(file, () => Now);
        store.Set("d1", Fcm, "P", "A", TheTwelveBefore);

        var again = new PushSubscriptionStore(file, () => Now);

        Assert.Equal(TheTwelveBefore, again.Get("d1")!.Kinds);
        Assert.DoesNotContain("BoostEnded", again.Get("d1")!.Kinds);
    }

    [Fact]
    public void Normalize_keeps_the_order()
    {
        Assert.Equal(["RankUp", "BoostEnded"], PushKinds.Normalize(["BoostEnded", "RankUp"]));
    }

    [Fact]
    public async Task The_notice_is_pushed_only_to_phones_that_chose_it()
    {
        var store = new PushSubscriptionStore(new MemoryPushFileStore(), () => Now);
        var handler = new FakePushHandler();
        Subscribe(store, "on", ["BoostEnded"]);
        Subscribe(store, "off", ["QueueFinished"]);
        using var dispatcher = new PushDispatcher(
            new PushSender(new HttpClient(handler), new Keys(VapidKey.Create()), () => Now, (_, _) => Task.CompletedTask), store);

        new PushNotifier(store, dispatcher).Publish(BoostNotices.Ended("Hero", "gold", Now));
        await dispatcher.StopAsync();

        var seen = Assert.Single(handler.Requests);
        Assert.EndsWith("/on", seen.Request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void The_payload_names_the_kind_and_opens_the_bot_section()
    {
        var message = PushPayload.Build(BoostNotices.Ended("Hero", "gold", Now));

        using var json = System.Text.Json.JsonDocument.Parse(message.Payload);
        Assert.Equal("BoostEnded", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("/#/character/Hero/bot", json.RootElement.GetProperty("url").GetString());
        Assert.Equal("Boost ended", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("The gold boost ended.", json.RootElement.GetProperty("body").GetString());
        Assert.Equal(PushUrgency.Normal, message.Urgency);
        Assert.Equal(3600, message.TtlSeconds);
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
