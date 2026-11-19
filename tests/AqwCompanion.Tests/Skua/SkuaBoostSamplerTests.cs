using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaBoostSamplerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly int[] Zero = [0, 0, 0, 0];
    private static readonly IReadOnlyList<SkuaBoostItem> None = [];

    private DateTimeOffset _t = T0;

    private SkuaBoostSampler Sampler() => new(() => _t);

    private static int[] Read(int gold = 0, int cls = 0, int rep = 0, int xp = 0) => [gold, cls, rep, xp];

    private static long Ms(DateTimeOffset t) => t.ToUnixTimeMilliseconds();

    [Fact]
    public void Logged_out_reports_nothing_and_forgets_everything()
    {
        Assert.Null(Sampler().Sample(Zero, false, []));
    }

    [Fact]
    public void A_login_the_sampler_saw_gives_exact_values()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);

        var first = s.Sample(Zero, true, []);
        Assert.Equal(new SkuaBoosts(0, 0, 0, 0, Ms(T0), []), first);

        _t += TimeSpan.FromSeconds(3);
        var next = s.Sample(Read(gold: 3600), true, []);
        Assert.NotNull(next);
        Assert.Equal(3600, next.Gold);
        Assert.Equal(Ms(_t), next.ReadAt);
    }

    [Fact]
    public void A_login_with_an_active_boost_at_the_first_sample_is_known()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);

        Assert.Equal(851, s.Sample(Read(gold: 851), true, [])!.Gold);
    }

    [Fact]
    public void A_plugin_started_after_the_login_reports_the_time_unknown()
    {
        var s = Sampler();
        Assert.Equal(-1, s.Sample(Read(gold: 851), true, [])!.Gold);

        _t += TimeSpan.FromSeconds(5);
        var next = s.Sample(Read(gold: 851, cls: 1200), true, []);
        Assert.Equal(-1, next!.Gold);
        Assert.Equal(1200, next.Class);
    }

    [Fact]
    public void The_same_values_return_the_same_reading_object()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        var first = s.Sample(Read(gold: 3600), true, []);

        _t += TimeSpan.FromSeconds(60);
        var second = s.Sample(Read(gold: 3600), true, []);

        Assert.Equal(first, second);
        Assert.Equal(first!.ReadAt, second!.ReadAt);
        Assert.Same(first, second);
    }

    [Fact]
    public void A_change_rebases_the_other_types_to_the_new_read_time()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        s.Sample(Read(gold: 3600), true, []);

        _t += TimeSpan.FromSeconds(100);
        var next = s.Sample(Read(gold: 3600, cls: 1200), true, []);

        Assert.Equal(3500, next!.Gold);
        Assert.Equal(1200, next.Class);
        Assert.Equal(Ms(T0 + TimeSpan.FromSeconds(100)), next.ReadAt);
    }

    [Fact]
    public void A_drop_to_zero_is_believed_on_a_second_zero_far_enough_after_the_first()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        var active = s.Sample(Read(gold: 840), true, []);

        _t += TimeSpan.FromSeconds(3);
        var held = s.Sample(Zero, true, []);
        Assert.Same(active, held);
        Assert.True(held!.Gold > 0);

        _t += SkuaBoostSampler.ZeroConfirm;
        var ended = s.Sample(Zero, true, []);
        Assert.Equal(0, ended!.Gold);
        Assert.NotEqual(active!.ReadAt, ended.ReadAt);
        Assert.Equal(Ms(_t), ended.ReadAt);
    }

    [Fact]
    public void A_zero_read_followed_by_the_old_value_resets_the_counter()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        var active = s.Sample(Read(gold: 840), true, []);

        _t += TimeSpan.FromSeconds(3);
        Assert.Same(active, s.Sample(Zero, true, []));

        _t += TimeSpan.FromSeconds(3);
        Assert.Same(active, s.Sample(Read(gold: 840), true, []));

        _t += TimeSpan.FromSeconds(3);
        Assert.Same(active, s.Sample(Zero, true, []));
        Assert.True(active!.Gold > 0);
    }

    [Fact]
    public void A_failed_read_keeps_the_last_values()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        var good = s.Sample(Read(gold: 3600), true, []);

        _t += TimeSpan.FromSeconds(3);
        Assert.Same(good, s.Sample(null, true, []));
    }

    [Fact]
    public void An_active_value_never_rebases_below_one_second()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        s.Sample(Read(gold: 10), true, []);

        _t += TimeSpan.FromSeconds(60);
        var next = s.Sample(Read(gold: 10, cls: 600), true, []);

        Assert.Equal(1, next!.Gold);
    }

    [Fact]
    public void Reset_forgets_and_makes_the_next_login_observed()
    {
        var s = Sampler();
        s.Sample(Read(gold: 5000), true, []);

        s.Reset();
        Assert.Equal(300, s.Sample(Read(gold: 300), true, [])!.Gold);
    }

    [Fact]
    public void A_fresh_sampler_first_called_logged_in_stays_unknown_after_a_gap()
    {
        var s = Sampler();
        s.NoteGap();

        Assert.Equal(-1, s.Sample(Read(gold: 300), true, [])!.Gold);
    }

    [Fact]
    public void A_gap_makes_a_change_unknown_but_keeps_what_did_not_change()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        s.Sample(Read(gold: 3600), true, []);

        s.NoteGap();
        _t += TimeSpan.FromSeconds(600);
        var next = s.Sample(Read(gold: 3600, cls: 1200), true, []);

        Assert.InRange(next!.Gold, 2990, 3010);
        Assert.Equal(3000, next.Gold);
        Assert.Equal(-1, next.Class);
    }

    [Fact]
    public void A_changed_item_list_gives_a_new_reading_with_rebased_values()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        var first = s.Sample(Read(gold: 3600), true, []);

        _t += TimeSpan.FromSeconds(30);
        var items = new[] { new SkuaBoostItem(17439, "GOLD Boost! (1 hr)", "gold", 3) };
        var next = s.Sample(Read(gold: 3600), true, items);

        Assert.NotSame(first, next);
        Assert.Equal(Ms(_t), next!.ReadAt);
        Assert.Equal(3570, next.Gold);
        Assert.Equal(items, next.Items);
    }

    [Fact]
    public void Negative_raw_values_read_as_zero()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);

        Assert.Equal(new SkuaBoosts(0, 0, 0, 0, Ms(T0), []), s.Sample(Read(gold: -5, cls: -1), true, []));
    }

    [Fact]
    public void A_raw_list_that_is_not_four_long_counts_as_a_failed_read()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        var good = s.Sample(Read(gold: 3600), true, []);

        _t += TimeSpan.FromSeconds(3);
        Assert.Same(good, s.Sample([1, 2, 3], true, []));
    }

    [Fact]
    public async Task Concurrent_NoteGap_and_Sample_never_throw()
    {
        var s = Sampler();
        var gaps = Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                s.NoteGap();
            }
        });
        var samples = Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                s.Sample(Read(gold: i % 7), i % 5 != 0, []);
            }
        });

        await Task.WhenAll(gaps, samples);
    }

    [Fact]
    public void Two_zeros_three_seconds_apart_do_not_end_the_boost()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        var active = s.Sample(Read(gold: 840), true, []);

        _t += TimeSpan.FromSeconds(3);
        Assert.Same(active, s.Sample(Zero, true, []));
        _t += TimeSpan.FromSeconds(3);
        Assert.Same(active, s.Sample(Zero, true, []));
        Assert.True(active!.Gold > 0);
    }

    [Fact]
    public void Two_zeros_six_seconds_apart_end_the_boost()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        s.Sample(Read(gold: 840), true, []);

        _t += TimeSpan.FromSeconds(3);
        s.Sample(Zero, true, []);
        _t += TimeSpan.FromSeconds(6);
        Assert.Equal(0, s.Sample(Zero, true, [])!.Gold);
    }

    [Fact]
    public void A_real_value_between_two_zeros_restarts_the_confirmation()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        var active = s.Sample(Read(gold: 840), true, []);

        _t += TimeSpan.FromSeconds(3);
        s.Sample(Zero, true, []);
        _t += TimeSpan.FromSeconds(3);
        s.Sample(Read(gold: 840), true, []);
        _t += TimeSpan.FromSeconds(3);
        Assert.Same(active, s.Sample(Zero, true, []));
        _t += TimeSpan.FromSeconds(3);
        Assert.Same(active, s.Sample(Zero, true, []));
        Assert.True(active!.Gold > 0);
    }

    [Fact]
    public void A_failed_first_read_reports_nothing_and_does_not_initialise()
    {
        var s = Sampler();
        s.Sample(Zero, false, []);
        Assert.Null(s.Sample(null, true, []));

        _t += TimeSpan.FromSeconds(3);
        Assert.Equal(851, s.Sample(Read(gold: 851), true, [])!.Gold);
    }

    [Fact]
    public void A_failed_first_read_on_a_sampler_that_never_saw_a_logout_then_a_value_stays_unknown()
    {
        var s = Sampler();
        Assert.Null(s.Sample(null, true, []));

        _t += TimeSpan.FromSeconds(3);
        Assert.Equal(-1, s.Sample(Read(gold: 851), true, [])!.Gold);
    }
}
