using AqwCompanion.App.Boosts;
using AqwCompanion.Phone.Boosts;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Boosts;

public sealed class SkuaBoostServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly SkuaBoostItem GoldItem = new(17439, "GOLD Boost! (1 hr)", "gold", 5);
    private static readonly SkuaBoostItem ClassItem = new(17437, "CLASS Boost! (1 hr)", "class", 2);
    private static readonly SkuaBoostItem OddItem = new(99, "Weird Boost", "unknown", 1);

    private static SkuaStatus Status(bool loggedIn, string? character, SkuaBoosts? boosts) =>
        new(loggedIn, character, "Artix", "map", null, false, null, [], [], "idle", Boosts: boosts);

    private static SkuaInstanceView Instance(string id, string label, SkuaStatus? status, SkuaHello? hello = null, bool disconnected = false) =>
        new(id, label, disconnected ? SkuaConnection.NotConnected : SkuaConnection.Connected, hello ?? new SkuaHello("0.9.0", "1.4.5.0", id), status, [], disconnected);

    private static SkuaBoosts Boosts(DateTimeOffset readAt, int gold = 0, int cls = 0, params SkuaBoostItem[] items) =>
        new(gold, cls, 0, 0, readAt.ToUnixTimeMilliseconds(), items);

    private static SkuaInstanceView Hero(SkuaBoosts? boosts, string id = "a1") => Instance(id, "Hero", Status(true, "Hero", boosts));

    private static SkuaInstanceView LoggedOut(string id = "a1", string version = "0.9.0") =>
        Instance(id, "Hero", Status(false, null, null), new SkuaHello(version, "1.4.5.0", id));

    private sealed class Rig : IDisposable
    {
        private readonly object _lock = new();
        private readonly List<Notice> _notices = [];
        private readonly List<Exception> _errors = [];
        private int _changes;

        public Rig(string appVersion = "0.9.0", Func<FakeSkua, ISkuaFeed>? feed = null, Action<Notice>? announce = null)
        {
            Skua = new FakeSkua();
            Now = T0;
            Service = new SkuaBoostService(
                feed?.Invoke(Skua) ?? Skua, Skua, () => Now, n =>
                {
                    lock (_lock)
                    {
                        _notices.Add(n);
                    }

                    announce?.Invoke(n);
                }, appVersion, e =>
                {
                    lock (_lock)
                    {
                        _errors.Add(e);
                    }
                });
            Service.Changed += (_, _) => Interlocked.Increment(ref _changes);
        }

        public FakeSkua Skua { get; }

        public DateTimeOffset Now { get; set; }

        public SkuaBoostService Service { get; }

        public int Changes => Volatile.Read(ref _changes);

        public IReadOnlyList<Notice> Notices
        {
            get
            {
                lock (_lock)
                {
                    return [.. _notices];
                }
            }
        }

        public IReadOnlyList<Exception> Errors
        {
            get
            {
                lock (_lock)
                {
                    return [.. _errors];
                }
            }
        }

        public void Feed(params SkuaInstanceView[] views) => Skua.Current = new SkuaHubView(views);

        public BoostView View(string id = "a1") => Service.Find(id)!;

        public void Dispose() => Service.Dispose();
    }

    [Fact]
    public void A_logged_in_status_with_boosts_makes_a_supported_view()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, gold: 840, items: [GoldItem, ClassItem, OddItem])));
        rig.Service.Pump();

        var view = Assert.Single(rig.Service.Views);
        Assert.Equal("a1", view.InstanceId);
        Assert.Equal("Hero", view.Character);
        Assert.True(view.Supported);
        Assert.True(view.Online);
        Assert.Equal(T0.ToUnixTimeMilliseconds(), view.ReadAt);
        Assert.Equal(840, view.Gold);
        Assert.Equal(0, view.Class);
        Assert.False(view.CanRelogin);
        Assert.Equal([false, true, false], view.Items.Select(i => i.CanUse));   // gold is active, class is free, the odd one has no known type
        Assert.Equal([17439, 17437, 99], view.Items.Select(i => i.ItemId));
        Assert.Same(view.InstanceId, rig.Service.Find("a1")!.InstanceId);
        Assert.Null(rig.Service.Find("nobody"));
    }

    [Fact]
    public void An_item_of_which_none_is_left_cannot_be_used()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, items: [ClassItem with { Quantity = 0 }])));
        rig.Service.Pump();

        Assert.False(Assert.Single(rig.View().Items).CanUse);
    }

    [Fact]
    public void Views_count_down_with_the_clock_without_any_event()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, gold: 840)));
        rig.Service.Pump();
        var changes = rig.Changes;

        rig.Now = T0.AddSeconds(90);

        Assert.Equal(750, rig.View().Gold);
        Assert.Equal(T0.AddSeconds(90).ToUnixTimeMilliseconds(), rig.View().ReadAt);
        Assert.Equal(changes, rig.Changes);
    }

    [Fact]
    public void A_logged_in_status_without_boosts_is_an_old_plugin_and_stays_so_when_it_logs_out()
    {
        using var rig = new Rig("0.9.0-dev.1");   // the "New DLL required" rule never complains in a development version
        rig.Feed(Hero(null));
        rig.Service.Pump();

        var view = rig.View();
        Assert.False(view.Supported);
        Assert.Empty(view.Items);
        Assert.False(view.CanRelogin);

        rig.Feed(LoggedOut());
        rig.Service.Pump();

        Assert.False(rig.View().Supported);
        Assert.False(rig.View().CanRelogin);
    }

    [Fact]
    public void A_logged_out_instance_is_supported_only_when_its_plugin_version_matches_the_app()
    {
        using var rig = new Rig();
        rig.Feed(LoggedOut("old", "0.8.0"), LoggedOut("new", "0.9.0"));
        rig.Service.Pump();

        Assert.False(rig.View("old").Supported);
        Assert.False(rig.View("old").CanRelogin);
        Assert.True(rig.View("new").Supported);
        Assert.True(rig.View("new").CanRelogin);
    }

    [Fact]
    public void Once_boosts_were_seen_the_instance_stays_supported_when_it_logs_out()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, gold: 100)));
        rig.Service.Pump();
        rig.Feed(LoggedOut("a1", "0.8.0"));   // even a hello that would be refused: the plugin already proved it can
        rig.Service.Pump();

        Assert.True(rig.View().Supported);
        Assert.True(rig.View().CanRelogin);
        Assert.False(rig.View().Online);
    }

    [Fact]
    public void Log_in_again_is_offered_only_when_logged_out_connected_and_supported()
    {
        using var rig = new Rig();
        rig.Feed(
            LoggedOut("out"),
            Instance("gone", "Hero", Status(false, null, null), disconnected: true),
            Hero(Boosts(T0, gold: 5), "in"));
        rig.Service.Pump();

        Assert.True(rig.View("out").CanRelogin);
        Assert.False(rig.View("gone").CanRelogin);
        Assert.False(rig.View("in").CanRelogin);
    }

    [Fact]
    public void The_end_of_a_boost_announces_one_notice_and_changes_the_view()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, gold: 840)));
        rig.Service.Pump();
        var changes = rig.Changes;

        rig.Now = T0.AddSeconds(900);
        rig.Feed(Hero(Boosts(rig.Now, gold: 0)));
        rig.Service.Pump();

        var notice = Assert.Single(rig.Notices);
        Assert.Equal(NoticeKind.BoostEnded, notice.Kind);
        Assert.Equal("Boost ended", notice.Title);
        Assert.Equal("The gold boost ended.", notice.Body);
        Assert.Equal("Hero", notice.Character);
        Assert.Equal(0, rig.View().Gold);
        Assert.Equal(changes + 1, rig.Changes);

        rig.Service.Pump();

        Assert.Single(rig.Notices);
        Assert.Equal(changes + 1, rig.Changes);
    }

    [Fact]
    public void No_notice_on_first_reading_login_logout_disconnect_or_character_change()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, gold: 0)));   // the first reading
        rig.Service.Pump();

        rig.Now = T0.AddSeconds(10);
        rig.Feed(LoggedOut());   // logout while a boost is active in memory is not an end
        rig.Service.Pump();
        rig.Now = T0.AddSeconds(20);
        rig.Feed(Hero(Boosts(rig.Now, gold: 600)));   // login with an active boost
        rig.Service.Pump();
        rig.Now = T0.AddSeconds(30);
        rig.Feed(Instance("a1", "Hero", Status(true, "Hero", Boosts(T0, gold: 600)), disconnected: true));   // the pipe closes
        rig.Service.Pump();
        rig.Now = T0.AddSeconds(40);
        rig.Feed(Hero(Boosts(rig.Now, gold: 0)));   // back, and the first reading after the break is the baseline
        rig.Service.Pump();
        rig.Now = T0.AddSeconds(50);
        rig.Feed(Hero(Boosts(rig.Now, gold: 300)));
        rig.Service.Pump();
        rig.Now = T0.AddSeconds(60);
        rig.Feed(Instance("a1", "Other", Status(true, "Other", Boosts(rig.Now, gold: 0))));   // another character on the same Skua
        rig.Service.Pump();

        Assert.Empty(rig.Notices);
        Assert.Equal("Other", rig.View().Character);
        Assert.Empty(rig.Skua.Sent);
    }

    [Fact]
    public async Task A_snapshot_that_reaches_the_lock_late_is_dropped_and_the_pump_looks_again()
    {
        var feed = new HoldFeed();
        using var rig = new Rig(feed: s =>
        {
            feed.Inner = s;
            return feed;
        });
        rig.Feed(Hero(Boosts(T0, gold: 3000)));
        rig.Service.Pump();

        // Thread A reads a reading of 900, then is held before it reaches the lock.
        rig.Feed(Hero(Boosts(T0.AddSeconds(1), gold: 900)));
        feed.Hold = new ManualResetEventSlim();
        feed.Held = new ManualResetEventSlim();
        var older = Task.Run(rig.Service.Pump);
        feed.Held.Wait();

        // Thread B reads the newer reading of 100 and applies it first.
        rig.Now = T0.AddSeconds(2);
        rig.Feed(Hero(Boosts(rig.Now, gold: 100)));
        rig.Service.Pump();
        feed.Hold.Set();
        await older.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(100, rig.View().Gold);   // the tracker did not go back to 900
        Assert.Empty(rig.Notices);
        Assert.Equal(4, feed.Reads);   // the first pump, the held read, B's read and the re-pump of the dropped one
    }

    [Fact]
    public void Changed_is_raised_only_for_a_structural_change()
    {
        using var rig = new Rig();
        Assert.Equal(0, rig.Changes);
        rig.Feed(Hero(Boosts(T0, gold: 840, items: [ClassItem])));
        rig.Service.Pump();
        Assert.Equal(1, rig.Changes);

        rig.Service.Pump();
        Assert.Equal(1, rig.Changes);   // the same feed

        rig.Now = T0.AddSeconds(30);
        rig.Feed(Hero(Boosts(rig.Now, gold: 810, items: [ClassItem])));   // a rebase: new readAt, same flags and items
        rig.Service.Pump();
        Assert.Equal(1, rig.Changes);

        rig.Feed(Hero(Boosts(rig.Now, gold: 810, cls: 600, items: [ClassItem])));   // an active flag
        rig.Service.Pump();
        Assert.Equal(2, rig.Changes);

        rig.Feed(Hero(Boosts(rig.Now, gold: 810, cls: 600, items: [ClassItem with { Quantity = 1 }])));   // an item
        rig.Service.Pump();
        Assert.Equal(3, rig.Changes);

        rig.Feed(LoggedOut());   // online, canRelogin
        rig.Service.Pump();
        Assert.Equal(4, rig.Changes);

        rig.Feed(Instance("a1", "Hero", Status(false, null, null), disconnected: true));   // canRelogin only
        rig.Service.Pump();
        Assert.Equal(5, rig.Changes);
    }

    [Fact]
    public void Supported_flipping_raises_Changed()
    {
        using var rig = new Rig();
        rig.Feed(LoggedOut("a1", "0.8.0"));
        rig.Service.Pump();
        var changes = rig.Changes;

        rig.Feed(Hero(Boosts(T0)));
        rig.Service.Pump();

        Assert.True(rig.Changes > changes);
        Assert.True(rig.View().Supported);
    }

    [Fact]
    public async Task Changed_and_the_announce_run_outside_the_lock()
    {
        SkuaBoostService? service = null;
        var visited = 0;
        using var rig = new Rig(announce: notice =>
        {
            _ = service!.Views;
            _ = service.Find("a1");
            service.Pump();
            Interlocked.Increment(ref visited);
        });
        service = rig.Service;
        service.Changed += (_, _) =>
        {
            _ = service.Views;
            _ = service.Find("a1");
            service.Pump();
            Interlocked.Increment(ref visited);
        };
        rig.Feed(Hero(Boosts(T0, gold: 840)));
        service.Pump();
        rig.Now = T0.AddSeconds(900);
        rig.Feed(Hero(Boosts(rig.Now, gold: 0)));

        await Task.Run(service.Pump).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(Volatile.Read(ref visited) >= 3);
        Assert.Empty(rig.Errors);
    }

    [Fact]
    public void A_throwing_announce_and_a_throwing_Changed_handler_never_escape_and_do_not_stop_each_other()
    {
        var announced = 0;
        using var rig = new Rig(announce: _ =>
        {
            announced++;
            throw new InvalidOperationException("announce");
        });
        var later = 0;
        rig.Service.Changed += (_, _) => throw new InvalidOperationException("changed");
        rig.Service.Changed += (_, _) => later++;
        rig.Feed(Hero(Boosts(T0, gold: 840)));
        rig.Service.Pump();
        var changesBefore = rig.Changes;
        rig.Now = T0.AddSeconds(900);
        rig.Feed(Hero(Boosts(rig.Now, gold: 0)));

        rig.Service.Pump();

        Assert.Equal(1, announced);
        Assert.Equal(2, later);   // the handler after the throwing one still ran, both times
        Assert.True(rig.Changes > changesBefore);
        Assert.Equal(0, rig.View().Gold);
    }

    [Fact]
    public void A_throwing_feed_is_reported_and_Pump_returns()
    {
        var feed = new HoldFeed();
        using var rig = new Rig(feed: s =>
        {
            feed.Inner = s;
            return feed;
        });
        rig.Feed(Hero(Boosts(T0, gold: 840)));
        rig.Service.Pump();

        feed.Throw = true;
        rig.Service.Pump();

        Assert.Single(rig.Errors);
        Assert.Equal(840, rig.View().Gold);
        Assert.Single(rig.Service.Views);
    }

    [Fact]
    public async Task A_throwing_feed_does_not_stop_a_command_from_answering()
    {
        var feed = new HoldFeed();
        using var rig = new Rig(feed: s =>
        {
            feed.Inner = s;
            return feed;
        });
        rig.Feed(Hero(Boosts(T0, items: [ClassItem])));
        rig.Service.Pump();
        feed.Throw = true;

        var result = await rig.Service.UseAsync("a1", ClassItem.ItemId);

        Assert.True(result.Ok);   // the decision is the memory's; the feed is only looked at after the answer, and its failure is reported
        Assert.Single(rig.Skua.Sent);
        Assert.Single(rig.Errors);
    }

    [Fact]
    public async Task UseAsync_checks_the_guards_before_anything_is_sent()
    {
        using var rig = new Rig();
        rig.Feed(
            Hero(Boosts(T0, gold: 840, items: [GoldItem, ClassItem, OddItem]), "ok"),
            Hero(null, "old"),
            LoggedOut("out"));
        rig.Service.Pump();

        Assert.Equal(BoostResult.Refused(BoostTexts.Disconnected), await rig.Service.UseAsync("nobody", ClassItem.ItemId));
        Assert.Equal(BoostResult.Refused(BoostTexts.NotSupported), await rig.Service.UseAsync("old", ClassItem.ItemId));
        Assert.Equal(BoostResult.Refused(BoostTexts.NotLoggedIn), await rig.Service.UseAsync("out", ClassItem.ItemId));
        Assert.Equal(BoostResult.Refused(BoostTexts.NotInBag), await rig.Service.UseAsync("ok", 12345));
        Assert.Equal(BoostResult.Refused(BoostTexts.UnknownType), await rig.Service.UseAsync("ok", OddItem.ItemId));
        Assert.Equal(BoostResult.Refused(BoostTexts.AlreadyActive("gold")), await rig.Service.UseAsync("ok", GoldItem.ItemId));

        Assert.Empty(rig.Skua.Sent);
    }

    [Fact]
    public async Task UseAsync_sends_one_useBoost_with_only_the_item_id()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, items: [ClassItem])));
        rig.Service.Pump();

        var result = await rig.Service.UseAsync("a1", ClassItem.ItemId);

        Assert.Equal(BoostResult.Done, result);
        var (instance, command) = Assert.Single(rig.Skua.Sent);
        Assert.Equal("a1", instance);
        Assert.Equal(new UseBoostCommand("", ClassItem.ItemId), command with { ReqId = "" });
    }

    [Fact]
    public async Task UseAsync_turns_every_answer_into_a_result()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, items: [ClassItem])));
        rig.Service.Pump();

        rig.Skua.AnswerWith(new ResultMessage("x", false, "The game did not activate the boost.", null, null, null, false, false));
        Assert.Equal(BoostResult.Refused("The game did not activate the boost."), await rig.Service.UseAsync("a1", ClassItem.ItemId));

        rig.Skua.AnswerWith(new ResultMessage("x", false, "  ", null, null, null, false, false));
        Assert.Equal(BoostResult.Refused(BoostTexts.Refused), await rig.Service.UseAsync("a1", ClassItem.ItemId));

        rig.Skua.Answer = (_, _) => Task.FromResult<ResultMessage?>(null);
        Assert.Equal(BoostResult.Unanswered, await rig.Service.UseAsync("a1", ClassItem.ItemId));

        rig.Skua.Answer = (_, _) => throw new KeyNotFoundException();
        Assert.Equal(BoostResult.Refused(BoostTexts.UnknownSkua), await rig.Service.UseAsync("a1", ClassItem.ItemId));

        rig.Skua.Answer = (_, _) => throw new InvalidOperationException();
        Assert.Equal(BoostResult.Refused(BoostTexts.Disconnected), await rig.Service.UseAsync("a1", ClassItem.ItemId));

        rig.Skua.Answer = (_, _) => throw new OperationCanceledException();
        Assert.Equal(BoostResult.Refused(BoostTexts.NoAnswer), await rig.Service.UseAsync("a1", ClassItem.ItemId));

        rig.Skua.Answer = (_, _) => throw new IOException("boom");
        Assert.Equal(BoostResult.Refused(BoostTexts.NoAnswer), await rig.Service.UseAsync("a1", ClassItem.ItemId));
        Assert.Single(rig.Errors);
    }

    [Fact]
    public async Task An_answer_is_matched_to_its_own_request_and_a_late_one_after_Dispose_is_returned_once_and_changes_nothing()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, items: [ClassItem])));
        rig.Service.Pump();
        var late = new TaskCompletionSource<ResultMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Skua.Answer = (_, _) => late.Task;
        var pending = rig.Service.UseAsync("a1", ClassItem.ItemId);
        var changes = rig.Changes;

        rig.Service.Dispose();
        rig.Feed(Hero(Boosts(T0.AddSeconds(5), cls: 3600, items: [ClassItem])));   // a feed change that no pump after Dispose may apply
        late.SetResult(new ResultMessage("x", true, null, null, null, null, false, false));

        Assert.Equal(BoostResult.Done, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(changes, rig.Changes);
        Assert.Equal(0, rig.View().Class);
        Assert.Empty(rig.Errors);
    }

    [Fact]
    public async Task ReloginAsync_refuses_before_anything_is_sent()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0), "in"), Hero(null, "old"), LoggedOut("away", "0.8.0"), Instance("gone", "Hero", Status(false, null, null), disconnected: true));
        rig.Service.Pump();

        Assert.Equal(BoostResult.Refused(BoostTexts.Disconnected), await rig.Service.ReloginAsync("nobody"));
        Assert.Equal(BoostResult.Refused(BoostTexts.AlreadyLoggedIn), await rig.Service.ReloginAsync("in"));
        Assert.Equal(BoostResult.Refused(BoostTexts.NotSupported), await rig.Service.ReloginAsync("old"));
        Assert.Equal(BoostResult.Refused(BoostTexts.NotSupported), await rig.Service.ReloginAsync("away"));
        Assert.Equal(BoostResult.Refused(BoostTexts.Disconnected), await rig.Service.ReloginAsync("gone"));

        Assert.Empty(rig.Skua.Sent);
    }

    [Fact]
    public async Task ReloginAsync_refuses_a_disconnected_supported_instance()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0)));
        rig.Service.Pump();
        rig.Feed(Instance("a1", "Hero", Status(false, null, null), disconnected: true));
        rig.Service.Pump();

        Assert.Equal(BoostResult.Refused(BoostTexts.Disconnected), await rig.Service.ReloginAsync("a1"));
        Assert.Empty(rig.Skua.Sent);
    }

    [Fact]
    public async Task ReloginAsync_sends_one_relogin_and_turns_the_answer_into_a_result()
    {
        using var rig = new Rig();
        rig.Feed(LoggedOut());
        rig.Service.Pump();

        Assert.Equal(BoostResult.Done, await rig.Service.ReloginAsync("a1"));
        var (instance, command) = Assert.Single(rig.Skua.Sent);
        Assert.Equal("a1", instance);
        Assert.IsType<ReloginCommand>(command);

        rig.Skua.AnswerWith(new ResultMessage("x", false, "Wait a moment before trying again.", null, null, null, false, false));
        Assert.Equal(BoostResult.Refused("Wait a moment before trying again."), await rig.Service.ReloginAsync("a1"));
        rig.Skua.Answer = (_, _) => Task.FromResult<ResultMessage?>(null);
        Assert.Equal(BoostResult.Unanswered, await rig.Service.ReloginAsync("a1"));
        rig.Skua.Answer = (_, _) => throw new KeyNotFoundException();
        Assert.Equal(BoostResult.Refused(BoostTexts.UnknownSkua), await rig.Service.ReloginAsync("a1"));
    }

    [Fact]
    public void Forget_of_the_hub_clears_the_memory()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, gold: 840)));
        rig.Service.Pump();
        var changes = rig.Changes;

        rig.Feed();   // what SkuaHub.Forget does: no instances
        rig.Service.Pump();

        Assert.Empty(rig.Service.Views);
        Assert.Null(rig.Service.Find("a1"));
        Assert.Equal(changes + 1, rig.Changes);

        rig.Now = T0.AddSeconds(900);
        rig.Feed(Hero(Boosts(rig.Now, gold: 0)));   // a cold start: no ended notice
        rig.Service.Pump();

        Assert.Empty(rig.Notices);
    }

    [Fact]
    public async Task A_logout_changes_nothing_in_the_queue_world()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, gold: 840)));
        rig.Service.Pump();
        rig.Feed(LoggedOut());
        rig.Service.Pump();

        Assert.Empty(rig.Skua.Sent);   // no stop, no pause, no command at all
        Assert.Empty(rig.Notices);

        var names = typeof(SkuaBoostService)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Select(f => f.FieldType)
            .Concat(typeof(SkuaBoostService).GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
            .Select(t => t.FullName ?? t.Name);
        Assert.DoesNotContain(names, n => n.Contains("Queue", StringComparison.OrdinalIgnoreCase));

        await rig.Service.UseAsync("a1", 1);
        await rig.Service.ReloginAsync("a1");
        Assert.DoesNotContain(rig.Skua.Sent, s => s.Command is StartCommand or StopCommand);
    }

    [Fact]
    public void Dispose_unsubscribes_and_Start_is_idempotent()
    {
        var feed = new HoldFeed();
        using var rig = new Rig(feed: s =>
        {
            feed.Inner = s;
            return feed;
        });

        rig.Service.Start();
        rig.Service.Start();
        Assert.Equal(1, feed.Subscribers);

        rig.Feed(Hero(Boosts(T0, gold: 840)));
        rig.Skua.Raise();
        Assert.Equal(840, rig.View().Gold);   // a feed change pumps

        rig.Service.Dispose();
        rig.Service.Dispose();
        Assert.Equal(0, feed.Subscribers);

        rig.Service.Start();   // after Dispose nothing subscribes again
        Assert.Equal(0, feed.Subscribers);
    }

    [Fact]
    public void Two_instances_are_independent_and_a_dropped_instance_leaves_the_views()
    {
        using var rig = new Rig();
        rig.Feed(Hero(Boosts(T0, gold: 840), "a1"), Instance("a2", "Mage", Status(true, "Mage", Boosts(T0, cls: 600))));
        rig.Service.Pump();

        Assert.Equal(2, rig.Service.Views.Count);
        Assert.Equal(840, rig.View("a1").Gold);
        Assert.Equal(0, rig.View("a2").Gold);
        Assert.Equal(600, rig.View("a2").Class);

        rig.Now = T0.AddSeconds(900);
        rig.Feed(Hero(Boosts(rig.Now, gold: 0), "a1"), Instance("a2", "Mage", Status(true, "Mage", Boosts(T0, cls: 600))));
        rig.Service.Pump();

        var notice = Assert.Single(rig.Notices);
        Assert.Equal("Hero", notice.Character);
        Assert.True(rig.View("a2").Class > 0);

        rig.Feed(Hero(Boosts(rig.Now, gold: 0), "a1"));
        rig.Service.Pump();

        Assert.Equal(["a1"], rig.Service.Views.Select(v => v.InstanceId));
        Assert.Null(rig.Service.Find("a2"));
    }

    /// <summary>A feed that can throw, counts its reads and subscribers, and holds the next read (after taking it) until released.</summary>
    internal sealed class HoldFeed : ISkuaFeed
    {
        private int _holdNext;
        private int _reads;
        private EventHandler? _changed;

        public ISkuaFeed Inner { get; set; } = null!;

        public bool Throw { get; set; }

        public ManualResetEventSlim? Hold { get; set; }

        public ManualResetEventSlim? Held { get; set; }

        public int Reads => Volatile.Read(ref _reads);

        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

        public SkuaHubView Current
        {
            get
            {
                Interlocked.Increment(ref _reads);
                if (Throw)
                {
                    throw new InvalidOperationException("feed");
                }

                var view = Inner.Current;
                if (Hold is { } gate && Interlocked.Exchange(ref _holdNext, 1) == 0)
                {
                    Held!.Set();
                    gate.Wait();
                }

                return view;
            }
        }

        public event EventHandler? Changed
        {
            add
            {
                _changed += value;
                Inner.Changed += value;
            }
            remove
            {
                _changed -= value;
                Inner.Changed -= value;
            }
        }
    }
}
