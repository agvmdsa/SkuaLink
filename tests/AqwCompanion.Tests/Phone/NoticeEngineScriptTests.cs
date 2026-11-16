using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Phone;

public class NoticeEngineScriptTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    internal static SkuaStatus Status(string character = "Hero", bool running = true, string? script = "Farm.cs", string? map = "battleon", SkuaProfile? profile = null,
        int? settle = null, string? control = null, bool loggedIn = true) =>
        new(loggedIn, character, "Artix", map, script, running, running ? T0 : null, Profile: profile, SettleSecondsLeft: settle, Control: control);

    internal static SkuaLogEntry Entry(DateTimeOffset at, SkuaLogKind kind, string text) => new(at, kind, text);

    internal static SkuaInstanceView Inst(string id, string label, SkuaStatus? status, IReadOnlyList<SkuaLogEntry>? newestFirst = null, bool disconnected = false) =>
        new(id, label, disconnected ? SkuaConnection.NotConnected : SkuaConnection.Connected, null, status, newestFirst ?? [], disconnected);

    internal static SkuaHubView View(params SkuaInstanceView[] instances) => new(instances);

    private static SkuaLogEntry Stopped(DateTimeOffset at, string script = "Farm.cs") => Entry(at, SkuaLogKind.ScriptStopped, "Script stopped: " + script);

    private static SkuaLogEntry Error(DateTimeOffset at, string message) => Entry(at, SkuaLogKind.ScriptError, "Script stopped with an error: " + message);

    [Fact]
    public void A_log_entry_already_there_at_the_first_view_raises_nothing()
    {
        var engine = new NoticeEngine();

        Assert.Empty(engine.ProcessSkua(View(Inst("a", "Hero", Status(), [Stopped(T0.AddMinutes(-1))])), T0));
    }

    [Fact]
    public void A_new_script_stopped_entry_raises_one_notice_with_the_script_name_and_never_twice()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status())), T0);
        var log = new[] { Stopped(T0.AddSeconds(30)) };

        var notice = Assert.Single(engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false), log)), T0.AddSeconds(31)));
        Assert.Empty(engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false), log)), T0.AddSeconds(32)));

        Assert.Equal(NoticeKind.ScriptStopped, notice.Kind);
        Assert.Equal($"skua-stopped:a:{T0.AddSeconds(30).UtcTicks}", notice.Id);
        Assert.Equal("Hero", notice.Character);
        Assert.Equal("Script stopped", notice.Title);
        Assert.Equal("Hero: Farm.cs stopped.", notice.Body);
    }

    [Fact]
    public void A_stop_the_phone_sent_within_60_seconds_is_not_announced_and_one_after_that_is()
    {
        var now = T0;
        var stops = new PhoneStops(() => now);
        var engine = new NoticeEngine(stops: stops);
        engine.ProcessSkua(View(Inst("a", "Hero", Status())), T0);
        now = T0.AddSeconds(10);
        stops.Note("a");

        Assert.Empty(engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false), [Stopped(T0.AddSeconds(12))])), T0.AddSeconds(12)));
        var later = engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false), [Stopped(T0.AddSeconds(100)), Stopped(T0.AddSeconds(12))])), T0.AddSeconds(100));

        Assert.Equal(NoticeKind.ScriptStopped, Assert.Single(later).Kind);
    }

    [Fact]
    public void A_stop_sent_to_another_instance_does_not_suppress_this_one()
    {
        var stops = new PhoneStops(() => T0);
        var engine = new NoticeEngine(stops: stops);
        engine.ProcessSkua(View(Inst("a", "Hero", Status()), Inst("b", "Zed", Status("Zed"))), T0);
        stops.Note("b");

        var notices = engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false), [Stopped(T0.AddSeconds(5))]), Inst("b", "Zed", Status("Zed"))), T0.AddSeconds(5));

        Assert.Equal("Hero", Assert.Single(notices).Character);
    }

    [Fact]
    public void A_script_error_names_the_script_and_the_first_line_cut_to_120_characters()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status())), T0);
        var message = new string('x', 130) + "\nsecond line";

        var notice = Assert.Single(engine.ProcessSkua(View(Inst("a", "Hero", Status(), [Error(T0.AddSeconds(5), message)])), T0.AddSeconds(5)));

        Assert.Equal(NoticeKind.ScriptError, notice.Kind);
        Assert.Equal("Script failed", notice.Title);
        Assert.Equal("Hero: Farm.cs failed: " + new string('x', 120), notice.Body);
        Assert.Equal($"skua-error:a:{T0.AddSeconds(5).UtcTicks}", notice.Id);
    }

    [Fact]
    public void An_error_and_the_stop_that_comes_with_it_raise_only_the_error_in_either_order()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status())), T0);

        var sameBatch = engine.ProcessSkua(View(Inst("a", "Hero", Status(), [Stopped(T0.AddSeconds(6)), Error(T0.AddSeconds(5), "boom")])), T0.AddSeconds(6));
        Assert.Equal(NoticeKind.ScriptError, Assert.Single(sameBatch).Kind);

        var next = engine.ProcessSkua(View(Inst("b", "Zed", Status("Zed"))), T0.AddSeconds(7));   // another instance appears: baseline
        Assert.Empty(next);
        engine.ProcessSkua(View(Inst("a", "Hero", Status()), Inst("b", "Zed", Status("Zed"), [Error(T0.AddSeconds(20), "boom")])), T0.AddSeconds(20));
        var stopAfter = engine.ProcessSkua(View(Inst("a", "Hero", Status()), Inst("b", "Zed", Status("Zed"), [Stopped(T0.AddSeconds(24)), Error(T0.AddSeconds(20), "boom")])), T0.AddSeconds(24));
        Assert.Empty(stopAfter);
    }

    [Theory]
    [InlineData(SkuaLogKind.LoggedOut, "Logged out")]
    [InlineData(SkuaLogKind.Relogin, "Disconnected by the server, logging in again")]
    public void A_logout_or_a_kick_while_a_script_ran_raises_CharacterKicked_once_per_five_minutes(SkuaLogKind kind, string text)
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status(running: true))), T0);

        var first = engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false, loggedIn: false), [Entry(T0.AddSeconds(5), kind, text)])), T0.AddSeconds(5));
        var again = engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false), [Entry(T0.AddMinutes(2), kind, text), Entry(T0.AddSeconds(5), kind, text)])), T0.AddMinutes(2));
        var later = engine.ProcessSkua(View(Inst("a", "Hero", Status(running: true), [Entry(T0.AddMinutes(7), kind, text)])), T0.AddMinutes(7));

        var notice = Assert.Single(first);
        Assert.Equal(NoticeKind.CharacterKicked, notice.Kind);
        Assert.Equal("Character disconnected", notice.Title);
        Assert.Equal("Hero was disconnected while a script was running.", notice.Body);
        Assert.Empty(again);
        Assert.Equal(NoticeKind.CharacterKicked, Assert.Single(later).Kind);
    }

    [Fact]
    public void A_logout_while_no_script_ran_and_a_plain_relogin_raise_nothing()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false))), T0);

        var idle = engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false), [Entry(T0.AddSeconds(5), SkuaLogKind.LoggedOut, "Logged out")])), T0.AddSeconds(5));
        engine.ProcessSkua(View(Inst("a", "Hero", Status(running: true))), T0.AddSeconds(6));
        var plain = engine.ProcessSkua(View(Inst("a", "Hero", Status(running: true), [Entry(T0.AddSeconds(8), SkuaLogKind.Relogin, "Logging in again")])), T0.AddSeconds(8));

        Assert.Empty(idle);
        Assert.Empty(plain);
    }

    private static IReadOnlyList<SkuaLogEntry> Deaths(int count, DateTimeOffset first, TimeSpan step) =>
        [.. Enumerable.Range(0, count).Select(i => Entry(first + step * i, SkuaLogKind.Died, "Died")).Reverse()];

    [Fact]
    public void Five_deaths_in_two_minutes_raise_one_notice_naming_the_character_script_and_map()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status())), T0);

        var four = engine.ProcessSkua(View(Inst("a", "Hero", Status(), Deaths(4, T0.AddSeconds(1), TimeSpan.FromSeconds(10)))), T0.AddSeconds(41));
        var five = engine.ProcessSkua(View(Inst("a", "Hero", Status(), Deaths(5, T0.AddSeconds(1), TimeSpan.FromSeconds(10)))), T0.AddSeconds(51));
        var six = engine.ProcessSkua(View(Inst("a", "Hero", Status(), Deaths(6, T0.AddSeconds(1), TimeSpan.FromSeconds(10)))), T0.AddSeconds(61));

        Assert.Empty(four);
        var notice = Assert.Single(five);
        Assert.Equal(NoticeKind.DyingOften, notice.Kind);
        Assert.Equal("Hero", notice.Character);
        Assert.Equal("Hero keeps dying", notice.Title);
        Assert.Equal("Hero died 5 times in 2 minutes (script Farm.cs, map battleon).", notice.Body);
        Assert.Empty(six);
    }

    [Fact]
    public void The_dying_text_leaves_out_the_script_and_the_map_when_they_are_not_known()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false, script: null, map: null))), T0);

        var notice = Assert.Single(engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false, script: null, map: null), Deaths(5, T0.AddSeconds(1), TimeSpan.FromSeconds(5)))), T0.AddSeconds(30)));

        Assert.Equal("Hero died 5 times in 2 minutes.", notice.Body);
    }

    [Fact]
    public void Deaths_spread_over_more_than_two_minutes_never_reach_five()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status())), T0);

        var notices = engine.ProcessSkua(View(Inst("a", "Hero", Status(), Deaths(5, T0.AddSeconds(1), TimeSpan.FromSeconds(40)))), T0.AddSeconds(170));

        Assert.Empty(notices);
    }

    [Fact]
    public void Dying_arms_again_only_after_two_clean_minutes_and_two_characters_are_counted_apart()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status()), Inst("b", "Zed", Status("Zed"))), T0);
        var first = Deaths(5, T0.AddSeconds(1), TimeSpan.FromSeconds(5));
        Assert.Single(engine.ProcessSkua(View(Inst("a", "Hero", Status(), first), Inst("b", "Zed", Status("Zed"), Deaths(2, T0.AddSeconds(1), TimeSpan.FromSeconds(5)))), T0.AddSeconds(30)));

        // Count falls below five (window passes) at +130 s: the clean time starts there.
        Assert.Empty(engine.Tick(T0.AddSeconds(130)));
        // Five new deaths 100 s later are still inside the clean time: no notice.
        var tooSoon = Deaths(5, T0.AddSeconds(200), TimeSpan.FromSeconds(2)).Concat(first).ToList();
        Assert.Empty(engine.ProcessSkua(View(Inst("a", "Hero", Status(), tooSoon), Inst("b", "Zed", Status("Zed"))), T0.AddSeconds(220)));
        // The count is clean from the first evaluation after the window passed (+350 s); two clean minutes later the engine is armed again.
        Assert.Empty(engine.Tick(T0.AddSeconds(350)));
        Assert.Empty(engine.Tick(T0.AddSeconds(500)));
        var again = Deaths(5, T0.AddSeconds(600), TimeSpan.FromSeconds(2)).Concat(tooSoon).ToList();
        var notices = engine.ProcessSkua(View(Inst("a", "Hero", Status(), again), Inst("b", "Zed", Status("Zed"))), T0.AddSeconds(610));
        Assert.Single(notices, n => n.Kind == NoticeKind.DyingOften);
    }

    [Fact]
    public void A_script_error_within_60_seconds_of_a_phone_stop_or_start_still_notifies()
    {
        var stops = new PhoneStops(() => T0);
        var engine = new NoticeEngine(stops: stops);
        engine.ProcessSkua(View(Inst("a", "Hero", Status())), T0);
        stops.Note("a");

        var notice = Assert.Single(engine.ProcessSkua(View(Inst("a", "Hero", Status(), [Error(T0.AddSeconds(5), "boom")])), T0.AddSeconds(5)));

        Assert.Equal(NoticeKind.ScriptError, notice.Kind);
    }

    [Fact]
    public void A_script_error_notifies_and_its_paired_stop_stays_quiet_even_after_a_phone_stop()
    {
        var stops = new PhoneStops(() => T0);
        var engine = new NoticeEngine(stops: stops);
        engine.ProcessSkua(View(Inst("a", "Hero", Status())), T0);
        stops.Note("a");

        var notices = engine.ProcessSkua(View(Inst("a", "Hero", Status(), [Stopped(T0.AddSeconds(6)), Error(T0.AddSeconds(5), "boom")])), T0.AddSeconds(6));

        Assert.Equal(NoticeKind.ScriptError, Assert.Single(notices).Kind);
    }

    [Fact]
    public void A_script_stopped_with_no_phone_stop_noted_is_not_suppressed()
    {
        var stops = new PhoneStops(() => T0);   // a phone start is never noted, so nothing is in the stops
        var engine = new NoticeEngine(stops: stops);
        engine.ProcessSkua(View(Inst("a", "Hero", Status())), T0);

        var notice = Assert.Single(engine.ProcessSkua(View(Inst("a", "Hero", Status(running: false), [Stopped(T0.AddSeconds(5))])), T0.AddSeconds(5)));

        Assert.Equal(NoticeKind.ScriptStopped, notice.Kind);
    }

    [Fact]
    public void One_character_is_one_key_whatever_the_case_or_the_instance_label()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Skua 1", Status("Hero"))), T0);
        var first = Deaths(3, T0.AddSeconds(1), TimeSpan.FromSeconds(5));
        engine.ProcessSkua(View(Inst("a", "Skua 1", Status("Hero"), first)), T0.AddSeconds(20));

        // The same character on another instance, spelled differently: its deaths count with the first ones.
        engine.ProcessSkua(View(Inst("a", "Skua 1", Status("Hero"), first), Inst("b", "Skua 2", Status("HERO"))), T0.AddSeconds(21));
        var notices = engine.ProcessSkua(View(Inst("a", "Skua 1", Status("Hero"), first), Inst("b", "Skua 2", Status("HERO"), Deaths(2, T0.AddSeconds(25), TimeSpan.FromSeconds(1)))), T0.AddSeconds(30));

        var notice = Assert.Single(notices, n => n.Kind == NoticeKind.DyingOften);
        Assert.Equal("HERO", notice.Character);
    }

    [Fact]
    public void Notices_name_the_status_character_and_not_the_instance_label()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Skua 1", Status("Hero"))), T0);

        var notice = Assert.Single(engine.ProcessSkua(View(Inst("a", "Skua 1", Status("Hero", running: false), [Stopped(T0.AddSeconds(5))])), T0.AddSeconds(5)));

        Assert.Equal("Hero", notice.Character);
        Assert.Equal("Hero: Farm.cs stopped.", notice.Body);
    }

    [Fact]
    public void Reset_forgets_cursors_so_the_next_view_is_a_baseline_again()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status())), T0);
        engine.Reset();

        Assert.Empty(engine.ProcessSkua(View(Inst("a", "Hero", Status(), [Stopped(T0.AddSeconds(5))])), T0.AddSeconds(6)));
    }

    [Fact]
    public void Every_pushable_kind_is_a_notice_kind()
    {
        Assert.All(AqwCompanion.Phone.Push.PushKinds.All, name => Assert.True(Enum.TryParse<NoticeKind>(name, out _), name));
    }
}
