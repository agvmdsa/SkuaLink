using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PhoneHubTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = T0;
    }

    private static FeedCharacter Hero(string name, DateTimeOffset? last = null) =>
        new(name, 60, true, T0, last, 1, new FeedFigures(1, 2, 3, 4, 5, 6), null);

    private static FeedSnapshot Base(string? selected = "Alpha", int rank = 8) =>
        new(1, new FeedCapture("Listening.", true), selected, [Hero("Alpha", T0)], [new FeedFaction("Yokai", rank, 0, 0, 0, false)], null);

    private static (PhoneHub Hub, FakeFeedSource Source, Clock Clock) NewHub(FeedSnapshot? initial = null)
    {
        var clock = new Clock();
        var source = new FakeFeedSource(initial ?? Base());
        var hub = new PhoneHub(source, new NoticeEngine(), new NoticeLog(50), () => clock.Now);
        return (hub, source, clock);
    }

    private static List<HubMessage> Drain(HubSubscription s)
    {
        var l = new List<HubMessage>();
        while (s.Reader.TryRead(out var m))
        {
            l.Add(m);
        }

        return l;
    }

    [Fact]
    public void A_new_hub_exposes_the_source_snapshot_and_no_notices()
    {
        var (hub, source, _) = NewHub();

        Assert.Equal(source.Current, hub.Current.Snapshot);
        Assert.Empty(hub.Current.Notices);
    }

    [Fact]
    public void A_changed_snapshot_publishes_one_state_message_with_the_new_selected_name()
    {
        var (hub, source, _) = NewHub();
        using var sub = hub.Subscribe();

        source.Current = Base(selected: "Bravo");
        source.Raise();

        var messages = Drain(sub);
        var message = Assert.Single(messages);
        Assert.Equal("state", message.Event);
        Assert.Contains("\"selected\":\"Bravo\"", message.Json);
    }

    [Fact]
    public void An_equal_snapshot_publishes_nothing()
    {
        var (hub, source, _) = NewHub();
        using var sub = hub.Subscribe();

        source.Current = Base();
        source.Raise();

        Assert.Empty(Drain(sub));
    }

    [Fact]
    public void A_rank_up_publishes_a_notice_then_a_state_and_is_kept_in_the_notices()
    {
        var (hub, source, clock) = NewHub();
        using var sub = hub.Subscribe();

        clock.Now = T0.AddSeconds(5);
        source.Current = Base(rank: 9);
        source.Raise();

        var messages = Drain(sub);
        Assert.Equal(["notice", "state"], messages.Select(m => m.Event));
        var notice = Assert.Single(hub.Current.Notices);
        Assert.Equal(NoticeKind.RankUp, notice.Kind);
    }

    private sealed class RecordingSink : INoticeSink
    {
        public List<Notice> Seen { get; } = [];
        public Exception? Throws { get; init; }
        public Action<Notice>? OnPublish { get; init; }

        public void Publish(Notice notice)
        {
            Seen.Add(notice);
            OnPublish?.Invoke(notice);
            if (Throws is not null)
            {
                throw Throws;
            }
        }
    }

    [Fact]
    public void A_published_notice_reaches_the_sink_after_the_log()
    {
        var clock = new Clock();
        var source = new FakeFeedSource(Base());
        PhoneHub? hub = null;
        var loggedWhenSunk = false;
        var sink = new RecordingSink { OnPublish = n => loggedWhenSunk = hub!.Current.Notices.Any(x => x.Id == n.Id) };
        hub = new PhoneHub(source, new NoticeEngine(), new NoticeLog(50), () => clock.Now, null, null, null, null, sink);

        clock.Now = T0.AddSeconds(5);
        source.Current = Base(rank: 9);
        source.Raise();

        var notice = Assert.Single(sink.Seen);
        Assert.Equal(NoticeKind.RankUp, notice.Kind);
        Assert.True(loggedWhenSunk);
        Assert.Equal(notice, Assert.Single(hub.Current.Notices));
    }

    [Fact]
    public void A_throwing_sink_does_not_stop_the_state_message_and_is_reported()
    {
        var clock = new Clock();
        var source = new FakeFeedSource(Base());
        var errors = new List<Exception>();
        var sink = new RecordingSink { Throws = new InvalidOperationException("sink") };
        using var hub = new PhoneHub(source, new NoticeEngine(), new NoticeLog(50), () => clock.Now, errors.Add, null, null, null, sink);
        using var sub = hub.Subscribe();

        clock.Now = T0.AddSeconds(5);
        source.Current = Base(rank: 9);
        source.Raise();

        Assert.Equal(["notice", "state"], Drain(sub).Select(m => m.Event));
        Assert.Single(errors);
    }

    [Fact]
    public void Tick_after_eleven_minutes_without_progress_publishes_one_notice_and_no_state()
    {
        var clock = new Clock();
        var skua = new FakeSkua { Current = new SkuaHubView([FakeSkua.Instance("a", "Hero", status: NoticeEngineScriptTests.Status(profile: null))]) };
        var hub = new PhoneHub(new FakeFeedSource(Base()), new NoticeEngine(), new NoticeLog(50), () => clock.Now, skua: skua);
        using var sub = hub.Subscribe();

        clock.Now = T0.AddMinutes(11);
        hub.Tick();
        var first = Drain(sub);
        hub.Tick();
        var second = Drain(sub);

        var message = Assert.Single(first);
        Assert.Equal("notice", message.Event);
        Assert.Contains("NoProgress", message.Json);
        Assert.Empty(second);
    }

    [Fact]
    public void A_changed_inventory_in_the_book_restarts_the_no_progress_timer()
    {
        var clock = new Clock();
        var book = new CharacterBook(new MemoryCharacterBookStore(), () => clock.Now);
        var skua = new FakeSkua { Current = new SkuaHubView([FakeSkua.Instance("a", "Hero", status: NoticeEngineScriptTests.Status(profile: null))]) };
        var hub = new PhoneHub(new FakeFeedSource(Base()), new NoticeEngine(), new NoticeLog(50), () => clock.Now, skua: skua, characters: book);
        using var sub = hub.Subscribe();

        clock.Now = T0.AddMinutes(3);
        book.OnInventory("Hero", new SkuaInventory("Hero", ["inv"], [new SkuaItem("Sword", 1, "inv")]));
        clock.Now = T0.AddMinutes(8);
        book.OnInventory("Hero", new SkuaInventory("Hero", ["inv"], [new SkuaItem("Sword", 2, "inv")]));
        Drain(sub);

        clock.Now = T0.AddMinutes(11);
        hub.Tick();
        Assert.DoesNotContain(Drain(sub), m => m.Event == "notice");
        clock.Now = T0.AddMinutes(19);
        hub.Tick();
        Assert.Single(Drain(sub), m => m.Event == "notice");
    }

    [Fact]
    public void Two_subscribers_receive_the_same_json_instance()
    {
        var (hub, source, _) = NewHub();
        using var a = hub.Subscribe();
        using var b = hub.Subscribe();

        source.Current = Base(selected: "Bravo");
        source.Raise();

        Assert.Same(Assert.Single(Drain(a)).Json, Assert.Single(Drain(b)).Json);
    }

    [Fact]
    public async Task Disposing_a_subscription_lowers_the_count_and_completes_its_reader()
    {
        var (hub, _, _) = NewHub();
        var sub = hub.Subscribe();
        Assert.Equal(1, hub.SubscriberCount);

        sub.Dispose();

        Assert.Equal(0, hub.SubscriberCount);
        await sub.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_hundred_changes_without_a_reader_keep_the_newest_sixty_four_and_never_block()
    {
        var (hub, source, _) = NewHub();
        using var sub = hub.Subscribe();

        for (var i = 0; i < 100; i++)
        {
            source.Current = Base(selected: $"Name{i}");
            source.Raise();
        }

        var messages = Drain(sub);
        Assert.Equal(64, messages.Count);
        Assert.Contains("\"selected\":\"Name99\"", messages[^1].Json);
        Assert.Contains("\"selected\":\"Name36\"", messages[0].Json);
    }

    [Fact]
    public void Reset_empties_the_notices_resets_the_engine_and_publishes_one_state()
    {
        var (hub, source, clock) = NewHub();
        clock.Now = T0.AddSeconds(5);
        source.Current = Base(rank: 9);
        source.Raise();
        Assert.Single(hub.Current.Notices);
        using var sub = hub.Subscribe();

        hub.Reset();

        Assert.Empty(hub.Current.Notices);
        var message = Assert.Single(Drain(sub));
        Assert.Equal("state", message.Event);

        // The engine forgot the rank-up id: going down and up again announces it anew.
        source.Current = Base(rank: 8);
        source.Raise();
        Assert.Empty(hub.Current.Notices);
        source.Current = Base(rank: 9);
        source.Raise();
        Assert.Single(hub.Current.Notices);
    }

    [Fact]
    public void Disposing_the_hub_unsubscribes_from_the_source()
    {
        var (hub, source, _) = NewHub();
        var sub = hub.Subscribe();
        Assert.Equal(1, source.Subscribers);

        hub.Dispose();
        using var live = hub.Subscribe();   // a late subscriber is still registered with the hub
        source.Current = Base(selected: "Bravo");
        source.Raise();

        Assert.Equal(0, source.Subscribers);
        Assert.Empty(Drain(live));
        Assert.Empty(Drain(sub));
    }

    private sealed class FlakySource(FeedSnapshot initial) : IFeedSource
    {
        private int _reads;

        public bool Throw { get; set; }

        public FeedSnapshot Current => Throw && _reads++ >= 0 ? throw new InvalidOperationException("boom") : initial;

        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void A_source_that_throws_on_the_second_read_does_not_throw_into_the_raiser_and_the_state_is_kept()
    {
        var source = new FlakySource(Base());
        var errors = new List<Exception>();
        using var hub = new PhoneHub(source, new NoticeEngine(), new NoticeLog(50), () => T0, errors.Add);
        using var sub = hub.Subscribe();
        var before = hub.Current;

        source.Throw = true;
        var raised = Record.Exception(source.Raise);

        Assert.Null(raised);
        Assert.Equal(before.Snapshot, hub.Current.Snapshot);
        Assert.Empty(Drain(sub));
        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        Assert.Null(Record.Exception(hub.Tick));
        Assert.Null(Record.Exception(hub.Reset));
        Assert.Equal(before.Snapshot, hub.Current.Snapshot);
    }

    [Fact]
    public void Serialize_uses_camel_case_string_enums_and_keeps_nulls()
    {
        var notice = new Notice("n1", NoticeKind.RankUp, "Alpha", "Rank up", "Yokai reached rank 9.", T0);
        var state = new PhoneState(Base(selected: null), [notice]);

        var json = PhoneJson.Serialize(state);

        Assert.Contains("\"selected\":null", json);
        Assert.Contains("\"kind\":\"RankUp\"", json);
        Assert.Contains("\"snapshot\":", json);
    }

    private sealed class FakeSkuaFeed : ISkuaFeed
    {
        private EventHandler? _changed;

        public SkuaHubView Value { get; set; } = SkuaHubView.Empty;

        public bool Throw { get; set; }

        public int Handlers { get; private set; }

        public SkuaHubView Current => Throw ? throw new InvalidOperationException("feed broke") : Value;

        public event EventHandler? Changed
        {
            add { _changed += value; Handlers++; }
            remove { _changed -= value; Handlers--; }
        }

        public void Raise() => _changed?.Invoke(this, EventArgs.Empty);
    }

    private static SkuaHubView ViewWith(string text, bool disconnected = false) => new([new SkuaInstanceView(
        "id-1",
        "Hero",
        disconnected ? SkuaConnection.NotConnected : SkuaConnection.Connected,
        new SkuaHello("0.1.0", "1.4.5.0"),
        new SkuaStatus(true, "Hero", "Artix", "battleon", "Farm.cs", true, T0),
        [new SkuaLogEntry(T0, SkuaLogKind.Sold, text)],
        disconnected)]);

    private static (PhoneHub Hub, FakeSkuaFeed Feed, List<Exception> Errors) NewSkuaHub()
    {
        var feed = new FakeSkuaFeed { Value = ViewWith("first") };
        var errors = new List<Exception>();
        var hub = new PhoneHub(new FakeFeedSource(Base()), new NoticeEngine(), new NoticeLog(50), () => T0, errors.Add, feed);
        return (hub, feed, errors);
    }

    [Fact]
    public void Without_a_skua_feed_the_skua_part_is_null_in_the_state_and_its_json()
    {
        var (hub, _, _) = NewHub();

        Assert.Null(hub.Current.Skua);
        Assert.Contains("\"skua\":null", hub.CurrentJson);
    }

    [Fact]
    public void With_a_skua_feed_the_state_carries_its_current_view()
    {
        var (hub, feed, _) = NewSkuaHub();

        Assert.Equal(feed.Current.Primary, hub.Current.Skua);
    }

    [Fact]
    public void A_skua_change_publishes_exactly_one_state_message_and_no_notice()
    {
        var (hub, feed, _) = NewSkuaHub();
        using var sub = hub.Subscribe();

        feed.Value = ViewWith("Sold Dragon Scale");
        feed.Raise();

        var message = Assert.Single(Drain(sub));
        Assert.Equal("state", message.Event);
        Assert.Contains("Sold Dragon Scale", message.Json);
    }

    [Fact]
    public void A_skua_feed_that_throws_is_reported_and_never_thrown_into_the_raiser()
    {
        var (hub, feed, errors) = NewSkuaHub();
        using var sub = hub.Subscribe();

        feed.Throw = true;
        Assert.Null(Record.Exception(feed.Raise));

        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        Assert.Empty(Drain(sub));

        feed.Throw = false;
        feed.Value = ViewWith("after");
        feed.Raise();
        Assert.Contains("after", Assert.Single(Drain(sub)).Json);
    }

    [Fact]
    public void Reset_keeps_the_skua_part()
    {
        var (hub, feed, _) = NewSkuaHub();
        using var sub = hub.Subscribe();

        hub.Reset();

        Assert.Equal(feed.Current.Primary, hub.Current.Skua);
        Assert.Contains("\"connection\":\"Connected\"", Assert.Single(Drain(sub)).Json);
    }

    [Fact]
    public void Dispose_unsubscribes_from_the_skua_feed()
    {
        var (hub, feed, _) = NewSkuaHub();
        Assert.Equal(1, feed.Handlers);

        hub.Dispose();

        Assert.Equal(0, feed.Handlers);
    }

    [Fact]
    public void A_skua_that_disconnects_and_comes_back_publishes_a_notice_each_time()
    {
        var (hub, feed, _) = NewSkuaHub();
        using var sub = hub.Subscribe();

        feed.Value = ViewWith("first", disconnected: true);
        feed.Raise();
        var off = Drain(sub);
        feed.Value = ViewWith("first");
        feed.Raise();
        var on = Drain(sub);

        Assert.Equal(["notice", "state"], off.Select(m => m.Event));
        Assert.Contains("\"kind\":\"SkuaDisconnected\"", off[0].Json);
        Assert.Equal(["notice", "state"], on.Select(m => m.Event));
        Assert.Contains("\"kind\":\"SkuaReconnected\"", on[0].Json);
        Assert.Equal([NoticeKind.SkuaReconnected, NoticeKind.SkuaDisconnected], hub.Current.Notices.Select(n => n.Kind));
    }

    [Fact]
    public void After_a_reset_the_skua_view_is_a_baseline_again()
    {
        var (hub, feed, _) = NewSkuaHub();
        hub.Reset();
        using var sub = hub.Subscribe();

        feed.Value = ViewWith("first", disconnected: true);
        feed.Raise();
        hub.Reset();
        Drain(sub);
        feed.Value = ViewWith("first", disconnected: true);
        feed.Raise();

        Assert.Equal(["state"], Drain(sub).Select(m => m.Event));
    }

    [Fact]
    public void With_a_character_book_the_state_lists_the_characters_and_a_book_change_publishes_it()
    {
        var book = new CharacterBook(new MemoryCharacterBookStore(), () => T0);
        var skua = new FakeSkua();
        var hub = new PhoneHub(new FakeFeedSource(Base()), new NoticeEngine(), new NoticeLog(50), () => T0, skua: skua, characters: book);
        using var sub = hub.Subscribe();
        book.OnStatus(new SkuaStatus(true, "Hero", null, null, null, false, null));
        Assert.Equal("Hero", Assert.Single(hub.Current.Characters!).Name);
        Assert.Contains(Drain(sub), m => m.Event == "state" && m.Json.Contains("\"characters\":[{\"name\":\"Hero\""));
    }

    [Fact]
    public void Without_a_character_book_the_state_has_no_characters()
    {
        Assert.Null(NewHub().Hub.Current.Characters);
    }

    [Fact]
    public void A_hard_farms_change_publishes_the_state()
    {
        var book = new CharacterBook(new MemoryCharacterBookStore(), () => T0);
        var check = new RaisingCheck();
        var hub = new PhoneHub(new FakeFeedSource(Base()), new NoticeEngine(), new NoticeLog(50), () => T0, characters: book, hardFarms: check);
        using var sub = hub.Subscribe();

        check.Raise();

        Assert.Equal(["state"], Drain(sub).Select(m => m.Event));
    }

    private sealed class RaisingCheck : IHardFarmsCheck
    {
        public event EventHandler? Changed;

        public FeedHardFarms? Check(AqwCompanion.Core.Inventory.CharacterInventory inventory) => null;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
