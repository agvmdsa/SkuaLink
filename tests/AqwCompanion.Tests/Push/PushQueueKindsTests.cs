using System.Buffers.Text;
using System.Security.Cryptography;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Push;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Push;

public class PushQueueKindsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private const string Fcm = "https://fcm.googleapis.com/fcm/send/abc";

    private sealed class Keys(VapidKey key) : IVapidKeySource
    {
        public VapidKey GetOrCreate() => key;

        public VapidKey? GetExisting() => key;
    }

    private static Notice Attention() =>
        new("queue-skip:Hero:1", NoticeKind.QueueAttention, "Hero", "Queue step skipped", "The step Dailies ended without Blood Orb. It may be a daily you already did today.", Now);

    private static Notice Finished() =>
        new("queue-finished:Hero:1", NoticeKind.QueueFinished, "Hero", "Queue finished", "Hero: the queue finished.", Now);

    [Fact]
    public void The_two_queue_kinds_follow_the_existing_ten_in_order()
    {
        Assert.Equal(13, PushKinds.All.Count);
        Assert.Equal("UpdateAvailable", PushKinds.All[9]);
        Assert.Equal(["QueueAttention", "QueueFinished"], PushKinds.All.Skip(10).Take(2).ToArray());
    }

    [Fact]
    public void A_new_subscription_has_both_queue_kinds_on()
    {
        var store = new PushSubscriptionStore(new MemoryPushFileStore(), () => Now);

        var sub = store.Set("d1", Fcm, "P", "A", null);

        Assert.Contains("QueueAttention", sub.Kinds);
        Assert.Contains("QueueFinished", sub.Kinds);
    }

    [Fact]
    public void Normalize_accepts_the_new_names_and_keeps_the_order()
    {
        Assert.Equal(["RankUp", "QueueAttention", "QueueFinished"], PushKinds.Normalize(["QueueFinished", "QueueAttention", "RankUp"]));
    }

    [Fact]
    public void The_payload_opens_the_characters_bot_section_and_is_not_urgent()
    {
        var message = PushPayload.Build(Attention());

        using var json = System.Text.Json.JsonDocument.Parse(message.Payload);
        Assert.Equal("/#/character/Hero/bot", json.RootElement.GetProperty("url").GetString());
        Assert.Equal("QueueAttention", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal(PushUrgency.Normal, message.Urgency);
        Assert.Equal(3600, message.TtlSeconds);
    }

    [Fact]
    public void The_notice_kinds_serialize_as_text_for_the_page()
    {
        Assert.Contains("\"kind\":\"QueueFinished\"", PhoneJsonText(Finished()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_a_phone_with_the_switch_on_gets_the_push()
    {
        var store = new PushSubscriptionStore(new MemoryPushFileStore(), () => Now);
        var handler = new FakePushHandler();
        Subscribe(store, "on", ["QueueAttention"]);
        Subscribe(store, "off", ["QueueFinished"]);
        using var dispatcher = new PushDispatcher(
            new PushSender(new HttpClient(handler), new Keys(VapidKey.Create()), () => Now, (_, _) => Task.CompletedTask), store);

        new PushNotifier(store, dispatcher).Publish(Attention());
        await dispatcher.StopAsync();

        var seen = Assert.Single(handler.Requests);
        Assert.EndsWith("/on", seen.Request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    private static string PhoneJsonText(Notice notice) => AqwCompanion.Phone.Hub.PhoneJson.SerializeNotice(notice);

    private static void Subscribe(PushSubscriptionStore store, string device, string[] kinds)
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = ua.ExportParameters(false).Q;
        store.Set(
            device, $"https://fcm.googleapis.com/fcm/send/{device}", Base64Url.EncodeToString([0x04, .. q.X!, .. q.Y!]),
            Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)), kinds);
    }
}
