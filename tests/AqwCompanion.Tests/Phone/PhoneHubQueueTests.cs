using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Queue;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PhoneHubQueueTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static FeedSnapshot Snapshot() =>
        new(1, new FeedCapture("Listening.", true), "Alpha", [new FeedCharacter("Alpha", 60, true, T0, T0, 1, new FeedFigures(1, 2, 3, 4, 5, 6), null)], [], null);

    private static QueueView View(string character = "Hero") =>
        new(
            character, [new QueueStep("s1", "0123456789abcdef", "Farm", "Blood Orb", 100)], 0, QueueState.Running, QueuePhase.Running, null, 37, true);

    private static (PhoneHub Hub, List<Exception> Errors) NewHub(FakeQueues? queues)
    {
        var errors = new List<Exception>();
        var hub = new PhoneHub(new FakeFeedSource(Snapshot()), new NoticeEngine(), new NoticeLog(50), () => T0, errors.Add, queues: queues);
        return (hub, errors);
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
    public void The_state_carries_the_queues_as_text_enums()
    {
        var fake = new FakeQueues();
        fake.Items.Add(View());
        var (hub, _) = NewHub(fake);

        var json = hub.CurrentJson;

        Assert.Contains("\"queues\":[{\"character\":\"Hero\"", json, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"Running\"", json, StringComparison.Ordinal);
        Assert.Contains("\"phase\":\"Running\"", json, StringComparison.Ordinal);
        Assert.Contains("\"have\":37", json, StringComparison.Ordinal);
        Assert.Contains("\"goalItem\":\"Blood Orb\"", json, StringComparison.Ordinal);
        Assert.Contains("\"canEdit\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_queue_feed_the_field_is_null()
    {
        var (hub, _) = NewHub(null);

        Assert.Contains("\"queues\":null", hub.CurrentJson, StringComparison.Ordinal);
    }

    [Fact]
    public void A_queue_change_sends_a_state_message_to_every_phone()
    {
        var fake = new FakeQueues();
        var (hub, _) = NewHub(fake);
        using var phone = hub.Subscribe();
        Drain(phone);

        fake.Items.Add(View());
        fake.Raise();

        var message = Assert.Single(Drain(phone));
        Assert.Equal("state", message.Event);
        Assert.Contains("\"queues\":[{\"character\":\"Hero\"", message.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disposed_hub_no_longer_listens()
    {
        var fake = new FakeQueues();
        var (hub, errors) = NewHub(fake);
        using var phone = hub.Subscribe();
        Drain(phone);

        hub.Dispose();
        fake.Items.Add(View());
        fake.Raise();

        Assert.Empty(Drain(phone));
        Assert.Empty(errors);
    }

    [Fact]
    public void A_throwing_queue_feed_never_escapes_the_event()
    {
        var fake = new FakeQueues();
        var (hub, errors) = NewHub(fake);
        using var phone = hub.Subscribe();
        Drain(phone);

        fake.ThrowOnViews = true;
        fake.Raise();

        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        Assert.Empty(Drain(phone));
    }
}
