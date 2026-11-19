using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaBoostResetTests
{
    [Fact]
    public void The_first_poll_of_a_plugin_loaded_mid_session_never_resets()
    {
        Assert.False(new SkuaBoostReset().Observe(true, "Hero"));
    }

    [Fact]
    public void One_or_two_logged_out_polls_do_not_reset()
    {
        var r = new SkuaBoostReset();
        r.Observe(true, "Hero");
        Assert.False(r.Observe(false, null));
        Assert.False(r.Observe(false, null));
        Assert.False(r.Observe(true, "Hero"));
    }

    [Fact]
    public void A_spurious_logged_out_poll_does_not_count_towards_a_later_run()
    {
        var r = new SkuaBoostReset();
        r.Observe(true, "Hero");
        r.Observe(false, null);
        r.Observe(true, "Hero");
        Assert.False(r.Observe(false, null));
        Assert.False(r.Observe(false, null));
    }

    [Fact]
    public void Three_logged_out_polls_in_a_row_reset_once()
    {
        var r = new SkuaBoostReset();
        r.Observe(true, "Hero");
        Assert.False(r.Observe(false, null));
        Assert.False(r.Observe(false, null));
        Assert.True(r.Observe(false, null));
        Assert.False(r.Observe(false, null));
        Assert.False(r.Observe(true, "Hero"));
    }

    [Fact]
    public void Another_character_resets_and_the_same_one_in_other_case_does_not()
    {
        var r = new SkuaBoostReset();
        r.Observe(true, "Hero");
        Assert.False(r.Observe(true, "HERO"));
        Assert.True(r.Observe(true, "Other"));
        Assert.False(r.Observe(true, "Other"));
    }
}
