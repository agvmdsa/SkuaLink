using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PhoneHubAnnounceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed class Sink : INoticeSink
    {
        public List<Notice> Seen { get; } = [];

        public bool Throw { get; set; }

        public void Publish(Notice notice)
        {
            Seen.Add(notice);
            if (Throw)
            {
                throw new InvalidOperationException("sink");
            }
        }
    }

    private static FeedSnapshot Snapshot() =>
        new(1, new FeedCapture("Listening.", true), "Alpha", [new FeedCharacter("Alpha", 60, true, T0, T0, 1, new FeedFigures(1, 2, 3, 4, 5, 6), null)], [], null);

    private static Notice Update() =>
        new("update:0.8.0", NoticeKind.UpdateAvailable, string.Empty, "SkuaLink update available", "SkuaLink 0.8.0 is available. Update it on the PC.", T0);

    private static (PhoneHub Hub, Sink Sink, List<Exception> Errors) NewHub()
    {
        var sink = new Sink();
        var errors = new List<Exception>();
        var hub = new PhoneHub(new FakeFeedSource(Snapshot()), new NoticeEngine(), new NoticeLog(50), () => T0, errors.Add, notices: sink);
        return (hub, sink, errors);
    }

    private static List<HubMessage> Drain(HubSubscription subscription)
    {
        var list = new List<HubMessage>();
        while (subscription.Reader.TryRead(out var message))
        {
            list.Add(message);
        }

        return list;
    }

    [Fact]
    public void An_announced_notice_is_logged_sunk_and_sent_to_every_phone_with_the_new_state()
    {
        var (hub, sink, _) = NewHub();
        using var phone = hub.Subscribe();
        Drain(phone);

        hub.Announce(Update());

        Assert.Equal(Update(), Assert.Single(hub.Current.Notices));
        Assert.Equal(Update(), Assert.Single(sink.Seen));
        var messages = Drain(phone);
        Assert.Equal(["notice", "state"], messages.Select(m => m.Event).ToArray());
        Assert.Contains("\"kind\":\"UpdateAvailable\"", messages[0].Json, StringComparison.Ordinal);
        Assert.Contains("update:0.8.0", messages[1].Json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failing_sink_is_reported_and_the_phones_still_get_the_notice()
    {
        var (hub, sink, errors) = NewHub();
        sink.Throw = true;
        using var phone = hub.Subscribe();
        Drain(phone);

        hub.Announce(Update());

        Assert.Single(errors);
        Assert.Contains(Drain(phone), m => m.Event == "notice");
    }

    [Fact]
    public void Reset_clears_an_announced_notice_from_the_log()
    {
        var (hub, _, _) = NewHub();
        hub.Announce(Update());

        hub.Reset();

        Assert.Empty(hub.Current.Notices);
    }
}
