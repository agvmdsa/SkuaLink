using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaInventoryScheduleTests
{
    private DateTimeOffset _now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private SkuaInventorySchedule New() => new(() => _now);

    private void Wait(double seconds) => _now = _now.AddSeconds(seconds);

    private static void Pass(SkuaInventorySchedule s, double took = 0.1)
    {
        s.Started();
        s.Finished(TimeSpan.FromSeconds(took));
    }

    [Fact]
    public void Never_read_is_due_at_once()
    {
        Assert.True(New().Due());
    }

    [Fact]
    public void A_request_is_due_only_after_the_minimum_gap()
    {
        var s = New();
        Pass(s);
        s.Request();
        Wait(4.9);
        Assert.False(s.Due());
        Wait(0.2);
        Assert.True(s.Due());
    }

    [Fact]
    public void Without_a_request_the_next_pass_is_the_periodic_one()
    {
        var s = New();
        Pass(s);
        Wait(299);
        Assert.False(s.Due());
        Wait(1);
        Assert.True(s.Due());
    }

    [Fact]
    public void A_slow_pass_widens_the_gap_ten_times_its_cost_up_to_the_periodic_interval()
    {
        var s = New();
        Pass(s, took: 2);
        Assert.Equal(TimeSpan.FromSeconds(20), s.Gap);
        Pass(s, took: 60);
        Assert.Equal(SkuaInventorySchedule.Periodic, s.Gap);
        Pass(s, took: 0.01);
        Assert.Equal(SkuaInventorySchedule.MinGap, s.Gap);
    }

    [Fact]
    public void Starting_a_pass_takes_the_request_and_a_request_during_it_stays()
    {
        var s = New();
        s.Request();
        s.Started();
        s.Request();
        s.Finished(TimeSpan.FromSeconds(0.1));
        Wait(5);
        Assert.True(s.Due());
    }

    [Fact]
    public void Reset_after_a_logout_makes_the_next_login_read_at_once()
    {
        var s = New();
        Pass(s);
        s.Reset();
        Assert.True(s.Due());
        Assert.Equal(SkuaInventorySchedule.MinGap, s.Gap);
    }

    [Fact]
    public void A_clock_that_went_back_is_due()
    {
        var s = New();
        Pass(s);
        Wait(-60);
        Assert.True(s.Due());
    }
}
