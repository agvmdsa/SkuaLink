using AqwCompanion.Phone.Boosts;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Boosts;

public sealed class BoostTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static SkuaBoosts R(int gold, int cls = 0, DateTimeOffset? at = null, int rep = 0, int xp = 0, params SkuaBoostItem[] items) =>
        new(gold, cls, rep, xp, (at ?? T0).ToUnixTimeMilliseconds(), items);

    private static IReadOnlyList<string> In(BoostTracker t, SkuaBoosts? boosts, DateTimeOffset now, string character = "Hero") =>
        t.Observe(new BoostInput(character, true, true, boosts), now);

    private static void Logout(BoostTracker t, DateTimeOffset now) => t.Observe(new BoostInput("Hero", false, true, null), now);

    [Fact]
    public void A_reading_counts_down_from_its_read_time()
    {
        var t = new BoostTracker();
        In(t, R(840, at: T0.AddSeconds(-60)), T0);
        Assert.Equal(780, t.Read(T0).Gold);
        Assert.Equal(770, t.Read(T0.AddSeconds(10)).Gold);
        Assert.Equal(780, t.Read(T0).Gold);
        Assert.Equal(770, t.Read(T0.AddSeconds(10)).Gold);
    }

    [Fact]
    public void Activation_from_zero_is_not_an_end()
    {
        var t = new BoostTracker();
        In(t, R(0), T0);
        Assert.Empty(In(t, R(3600, at: T0.AddSeconds(5)), T0.AddSeconds(5)));
        Assert.Equal(3600, t.Read(T0.AddSeconds(5)).Gold);
    }

    [Fact]
    public void A_login_with_an_active_boost_counts_from_the_reading()
    {
        var t = new BoostTracker();
        In(t, R(1000), T0);
        Logout(t, T0.AddSeconds(10));
        var later = T0.AddSeconds(100);
        In(t, R(500, at: later.AddSeconds(-20)), later);
        Assert.Equal(480, t.Read(later).Gold);
        Assert.Equal(470, t.Read(later.AddSeconds(10)).Gold);
    }

    [Fact]
    public void Expiry_to_zero_ends_exactly_once()
    {
        var t = new BoostTracker();
        In(t, R(840), T0);
        var at = T0.AddSeconds(900);
        Assert.Equal(["gold"], In(t, R(0, at: at), at));
        Assert.Empty(In(t, R(0, at: at), at.AddSeconds(3)));
        Assert.Empty(In(t, R(0, at: at.AddSeconds(6)), at.AddSeconds(6)));
    }

    [Fact]
    public void Logout_pauses_the_time()
    {
        var t = new BoostTracker();
        In(t, R(1000), T0);
        Logout(t, T0.AddSeconds(10));
        Assert.Equal(t.Read(T0.AddSeconds(10)).Gold, t.Read(T0.AddSeconds(500)).Gold);
        Assert.False(t.Read(T0.AddSeconds(500)).Online);
        var back = T0.AddSeconds(600);
        In(t, R(700, at: back), back);
        Assert.Equal(700, t.Read(back).Gold);
    }

    [Fact]
    public void Disconnect_pauses_the_online_time_and_the_same_reading_resent_is_anchored_again_from_its_read_time()
    {
        var t = new BoostTracker();
        var reading = R(1000);
        In(t, reading, T0);
        t.Observe(new BoostInput("Hero", true, false, null), T0.AddSeconds(10));
        Assert.Equal(990, t.Read(T0.AddSeconds(500)).Gold);
        var back = T0.AddSeconds(500);
        t.Observe(new BoostInput("Hero", true, true, R(1000)), back);
        Assert.Equal(500, t.Read(back).Gold);
        Assert.Equal(490, t.Read(back.AddSeconds(10)).Gold);
    }

    [Fact]
    public void The_first_reading_never_ends_anything()
    {
        var t = new BoostTracker();
        Assert.Empty(In(t, R(0), T0));
        Assert.Empty(In(t, R(0, at: T0.AddSeconds(3)), T0.AddSeconds(3)));
        var u = new BoostTracker();
        Assert.Empty(In(u, R(500), T0));
        Assert.Equal(["gold"], In(u, R(0, at: T0.AddSeconds(3)), T0.AddSeconds(3)));
    }

    [Fact]
    public void Login_logout_disconnect_and_character_change_are_baselines()
    {
        var t = new BoostTracker();
        In(t, R(100), T0);
        Logout(t, T0.AddSeconds(1));
        Assert.Empty(In(t, R(0, at: T0.AddSeconds(2)), T0.AddSeconds(2)));

        In(t, R(100, at: T0.AddSeconds(3)), T0.AddSeconds(3));
        Assert.Equal(["gold"], In(t, R(0, at: T0.AddSeconds(6)), T0.AddSeconds(6)));

        var u = new BoostTracker();
        In(u, R(100), T0);
        u.Observe(new BoostInput("Hero", true, false, null), T0.AddSeconds(1));
        Assert.Empty(u.Observe(new BoostInput("Hero", true, true, R(0, at: T0.AddSeconds(2))), T0.AddSeconds(2)));
        In(u, R(100, at: T0.AddSeconds(3)), T0.AddSeconds(3));
        Assert.Equal(["gold"], In(u, R(0, at: T0.AddSeconds(6)), T0.AddSeconds(6)));

        var v = new BoostTracker();
        In(v, R(100), T0);
        Assert.Empty(In(v, R(0, at: T0.AddSeconds(1)), T0.AddSeconds(1), "Other"));
        Assert.Equal(0, v.Read(T0.AddSeconds(1)).Gold);
        In(v, R(100, at: T0.AddSeconds(3)), T0.AddSeconds(3), "Other");
        Assert.Equal(["gold"], In(v, R(0, at: T0.AddSeconds(6)), T0.AddSeconds(6), "Other"));

        var w = new BoostTracker();
        In(w, R(100, items: new SkuaBoostItem(1, "GOLD Boost!", "gold", 2)), T0);
        In(w, R(0), T0.AddSeconds(1), "Other");
        Assert.Empty(w.Read(T0.AddSeconds(1)).Items);
    }

    [Fact]
    public void An_unknown_time_stays_unknown_and_can_end()
    {
        var t = new BoostTracker();
        In(t, R(-1), T0);
        Assert.Equal(-1, t.Read(T0).Gold);
        Assert.Equal(-1, t.Read(T0.AddHours(5)).Gold);
        Assert.True(t.Read(T0).Active("gold"));
        Assert.Equal(["gold"], In(t, R(0, at: T0.AddSeconds(30)), T0.AddSeconds(30)));
    }

    [Fact]
    public void The_value_is_clamped_at_one_while_active()
    {
        var t = new BoostTracker();
        In(t, R(5, at: T0.AddSeconds(-100)), T0);
        Assert.Equal(1, t.Read(T0).Gold);
        Assert.True(t.Read(T0).Active("gold"));
        Assert.Equal(1, t.Read(T0.AddHours(1)).Gold);
    }

    [Fact]
    public void Several_types_in_one_reading_end_in_type_order()
    {
        var t = new BoostTracker();
        In(t, R(10, 20, rep: 30, xp: 40), T0);
        Assert.Equal(["gold", "class", "rep", "xp"], In(t, R(0, at: T0.AddSeconds(5)), T0.AddSeconds(5)));
        var u = new BoostTracker();
        In(u, R(0, 20), T0);
        Assert.Equal(["class"], In(u, R(0, at: T0.AddSeconds(5)), T0.AddSeconds(5)));
    }

    [Fact]
    public void A_rebased_reading_for_the_other_types_keeps_them_consistent()
    {
        var t = new BoostTracker();
        In(t, R(3600, 0), T0);
        var at = T0.AddSeconds(100);
        In(t, R(3500, 1800, at), at);
        Assert.Equal(3500, t.Read(at).Gold);
        Assert.Equal(3440, t.Read(T0.AddSeconds(160)).Gold);
        Assert.Equal(1800 - 60, t.Read(T0.AddSeconds(160)).Class);
    }

    [Fact]
    public void The_items_come_from_the_last_reading()
    {
        var t = new BoostTracker();
        var item = new SkuaBoostItem(17439, "GOLD Boost! (1 hr)", "gold", 33);
        In(t, R(0, items: item), T0);
        Assert.Equal([item], t.Read(T0).Items);
        In(t, R(0, at: T0.AddSeconds(3)), T0.AddSeconds(3));
        Assert.Empty(t.Read(T0).Items);
    }

    [Fact]
    public void A_status_without_boosts_changes_nothing()
    {
        var t = new BoostTracker();
        Assert.Empty(In(t, null, T0));
        var read = t.Read(T0);
        Assert.False(read.Known);
        Assert.True(read.Online);
        In(t, R(100), T0.AddSeconds(1));
        Assert.Empty(In(t, null, T0.AddSeconds(2)));
        Assert.True(t.Read(T0.AddSeconds(2)).Known);
        Assert.Equal(98, t.Read(T0.AddSeconds(2)).Gold);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    [InlineData(-5L)]
    [InlineData(long.MinValue)]
    public void A_read_time_in_the_future_or_absurd_never_throws(long readAt)
    {
        var t = new BoostTracker();
        var boosts = new SkuaBoosts(100, 0, 0, 0, readAt);
        var ex = Record.Exception(() => { In(t, boosts, T0); t.Read(T0); t.Read(T0.AddDays(1)); });
        Assert.Null(ex);
        Assert.InRange(t.Read(T0).Gold, 1, 100);
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var t = new BoostTracker();
        In(t, R(100, items: new SkuaBoostItem(1, "GOLD Boost!", "gold", 1)), T0);
        t.Reset();
        var read = t.Read(T0);
        Assert.False(read.Known);
        Assert.False(read.Online);
        Assert.Equal(0, read.Gold);
        Assert.Empty(read.Items);
        Assert.Empty(In(t, R(0, at: T0.AddSeconds(3)), T0.AddSeconds(3)));
    }

    [Fact]
    public void A_reconnect_with_an_identical_reading_then_a_real_expiry_ends_exactly_once()
    {
        var t = new BoostTracker();
        var reading = R(840);
        In(t, reading, T0);
        t.Observe(new BoostInput("Hero", true, false, null), T0.AddSeconds(10));
        var back = T0.AddSeconds(20);
        Assert.Empty(t.Observe(new BoostInput("Hero", true, true, reading), back));

        var at = T0.AddSeconds(900);
        Assert.Equal(["gold"], In(t, R(0, at: at), at));
        Assert.Empty(In(t, R(0, at: at.AddSeconds(6)), at.AddSeconds(6)));
    }

    [Fact]
    public void The_countdown_is_re_anchored_when_the_same_reading_comes_back_after_an_outage()
    {
        var t = new BoostTracker();
        var reading = R(1000);
        In(t, reading, T0);
        t.Observe(new BoostInput("Hero", true, false, null), T0.AddSeconds(10));
        var back = T0.AddSeconds(500);
        t.Observe(new BoostInput("Hero", true, true, reading), back);

        // The game timer ran the whole 500 s while the pipe was away; online time paused, so the reading is re-anchored.
        Assert.Equal(500, t.Read(back).Gold);
        Assert.Equal(490, t.Read(back.AddSeconds(10)).Gold);
    }
}
