using System.Text.Json;
using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Boosts;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PhoneHubBoostTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static FeedSnapshot Snapshot() =>
        new(1, new FeedCapture("Listening.", true), "Alpha", [new FeedCharacter("Alpha", 60, true, T0, T0, 1, new FeedFigures(1, 2, 3, 4, 5, 6), null)], [], null);

    private static BoostView FixtureView() =>
        new(
            "a1", "Hero", true, true, 1763481234567, 840, -1, 0, 0,
            [
                new BoostItemView(17439, "GOLD Boost! (1 hr)", "gold", 33, false),
                new BoostItemView(22447, "Daily Login Class Boost (20 min)", "class", 61, false),
                new BoostItemView(17437, "CLASS Boost! (1 hr)", "class", 70, false),
            ],
            false);

    private static (PhoneHub Hub, List<Exception> Errors) NewHub(FakeBoosts? boosts)
    {
        var errors = new List<Exception>();
        var hub = new PhoneHub(new FakeFeedSource(Snapshot()), new NoticeEngine(), new NoticeLog(50), () => T0, errors.Add, boosts: boosts);
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

    private static string BoostsOf(PhoneHub hub) => JsonDocument.Parse(hub.CurrentJson).RootElement.GetProperty("boosts").GetRawText();

    [Fact]
    public void The_state_json_carries_the_boosts_exactly_as_the_page_fixture_says()
    {
        var fake = new FakeBoosts();
        fake.Items.Add(FixtureView());
        var (hub, _) = NewHub(fake);

        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Phone", "fixtures", "boosts-state.json")).Trim();

        Assert.Equal(expected, BoostsOf(hub));
    }

    [Fact]
    public void Only_supported_views_are_in_the_state()
    {
        var fake = new FakeBoosts();
        fake.Items.Add(FixtureView() with { InstanceId = "old", Supported = false });
        var (hub, _) = NewHub(fake);

        Assert.Equal("[]", BoostsOf(hub));

        fake.Items.Add(FixtureView());
        using var doc = JsonDocument.Parse(hub.CurrentJson);
        var list = doc.RootElement.GetProperty("boosts");
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("a1", list[0].GetProperty("instanceId").GetString());
    }

    [Fact]
    public void Without_a_boost_feed_the_field_is_null()
    {
        var (hub, _) = NewHub(null);

        Assert.Contains("\"boosts\":null", hub.CurrentJson, StringComparison.Ordinal);
    }

    [Fact]
    public void A_boost_change_sends_a_state_message_to_every_phone()
    {
        var fake = new FakeBoosts();
        var (hub, _) = NewHub(fake);
        using var phone = hub.Subscribe();
        Drain(phone);

        fake.Items.Add(FixtureView());
        fake.Raise();

        var message = Assert.Single(Drain(phone));
        Assert.Equal("state", message.Event);
        Assert.Contains("\"boosts\":[{\"instanceId\":\"a1\"", message.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disposed_hub_no_longer_listens()
    {
        var fake = new FakeBoosts();
        var (hub, errors) = NewHub(fake);
        using var phone = hub.Subscribe();
        Drain(phone);
        Assert.Equal(1, fake.SubscriberCount);

        hub.Dispose();
        fake.Items.Add(FixtureView());
        fake.Raise();

        Assert.Equal(0, fake.SubscriberCount);
        Assert.Empty(Drain(phone));
        Assert.Empty(errors);
    }

    [Fact]
    public void A_throwing_boost_feed_never_escapes_the_event()
    {
        var fake = new FakeBoosts();
        var (hub, errors) = NewHub(fake);
        using var phone = hub.Subscribe();
        Drain(phone);

        fake.ThrowOnViews = true;
        fake.Raise();

        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        var message = Assert.Single(Drain(phone));
        Assert.Contains("\"boosts\":null", message.Json, StringComparison.Ordinal);
    }
}
