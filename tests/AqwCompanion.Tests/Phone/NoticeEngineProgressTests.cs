using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using static AqwCompanion.Tests.Phone.NoticeEngineScriptTests;

namespace AqwCompanion.Tests.Phone;

public class NoticeEngineProgressTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static SkuaProfile Profile(long xp = 100) => new(60, xp, 5000, 1200, 7, 10, true);

    private static SkuaHubView Hero(SkuaStatus status) => View(Inst("a", "Hero", status));

    private static NoticeEngine Started(SkuaStatus? status = null)
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(Hero(status ?? Status(profile: Profile())), T0);
        return engine;
    }

    [Fact]
    public void No_change_for_ten_minutes_raises_one_notice_once_per_stall()
    {
        var engine = Started();

        Assert.Empty(engine.Tick(T0.AddMinutes(9)));
        var notice = Assert.Single(engine.Tick(T0.AddMinutes(10)));
        Assert.Empty(engine.Tick(T0.AddMinutes(11)));
        Assert.Empty(engine.ProcessSkua(Hero(Status(profile: Profile())), T0.AddMinutes(12)));

        Assert.Equal(NoticeKind.NoProgress, notice.Kind);
        Assert.Equal("Hero", notice.Character);
        Assert.Equal($"noprogress:Hero:{T0.UtcTicks}", notice.Id);
        Assert.Equal("No progress", notice.Title);
        Assert.Equal("Hero has not made progress for 10 minutes.", notice.Body);
    }

    [Fact]
    public void Any_progress_signal_restarts_the_timer_and_re_arms_the_notice()
    {
        var engine = Started();
        Assert.Single(engine.Tick(T0.AddMinutes(10)));

        engine.ProcessSkua(Hero(Status(profile: Profile(xp: 101))), T0.AddMinutes(11));   // experience moved
        Assert.Empty(engine.Tick(T0.AddMinutes(20)));
        var second = Assert.Single(engine.Tick(T0.AddMinutes(21)));

        Assert.Equal($"noprogress:Hero:{T0.AddMinutes(11).UtcTicks}", second.Id);
    }

    [Theory]
    [InlineData("map")]
    [InlineData("script")]
    [InlineData("running")]
    [InlineData("gold")]
    public void The_map_the_script_state_and_the_numbers_count_as_progress(string what)
    {
        var engine = Started();
        var changed = what switch
        {
            "map" => Status(profile: Profile(), map: "yulgar"),
            "script" => Status(profile: Profile(), script: "Other.cs"),
            "running" => Status(profile: Profile(), running: false),
            _ => Status(profile: Profile() with { Gold = 5 }),
        };

        engine.ProcessSkua(Hero(changed), T0.AddMinutes(5));

        Assert.Empty(engine.Tick(T0.AddMinutes(14)));
        Assert.Single(engine.Tick(T0.AddMinutes(15)));
    }

    [Fact]
    public void A_reputation_change_counts_as_progress()
    {
        var engine = Started(Status(profile: Profile()) with { Factions = [new SkuaFaction(1, "Yokai", 8, 100, 5, 50)] });

        engine.ProcessSkua(Hero(Status(profile: Profile()) with { Factions = [new SkuaFaction(1, "Yokai", 8, 101, 6, 50)] }), T0.AddMinutes(5));

        Assert.Empty(engine.Tick(T0.AddMinutes(14)));
    }

    [Fact]
    public void Control_settle_and_options_noise_do_not_count_as_progress()
    {
        var engine = Started();

        engine.ProcessSkua(Hero(Status(profile: Profile(), settle: 5, control: "settling")), T0.AddMinutes(5));
        engine.ProcessSkua(Hero(Status(profile: Profile()) with { OptionsStuck = true }), T0.AddMinutes(6));

        Assert.Single(engine.Tick(T0.AddMinutes(10)));
    }

    [Fact]
    public void A_changed_inventory_snapshot_counts_as_progress()
    {
        var engine = Started();
        var one = new CharacterRecord("Hero", T0, Inventory: new SkuaInventory("Hero", ["inv"], [new SkuaItem("Sword", 1, "inv")]));
        var two = new CharacterRecord("Hero", T0, Inventory: new SkuaInventory("Hero", ["inv"], [new SkuaItem("Sword", 2, "inv")]));

        engine.NoteProgress([one], T0.AddMinutes(3));   // first sight of the inventory: only remembered
        engine.NoteProgress([one], T0.AddMinutes(4));
        Assert.Single(engine.Tick(T0.AddMinutes(10)));
        engine.NoteProgress([two], T0.AddMinutes(11));

        Assert.Empty(engine.Tick(T0.AddMinutes(20)));
        Assert.Single(engine.Tick(T0.AddMinutes(21)));
    }

    [Fact]
    public void A_disconnected_or_logged_out_character_is_not_watched_and_starts_over_when_back()
    {
        var engine = Started();

        engine.ProcessSkua(View(Inst("a", "Hero", Status(profile: Profile()), disconnected: true)), T0.AddMinutes(5));
        Assert.Empty(engine.Tick(T0.AddMinutes(30)));
        engine.ProcessSkua(Hero(Status(profile: Profile())), T0.AddMinutes(31));
        Assert.Empty(engine.Tick(T0.AddMinutes(40)));
        Assert.Single(engine.Tick(T0.AddMinutes(41)));
        engine.ProcessSkua(Hero(Status(profile: Profile(), loggedIn: false)), T0.AddMinutes(42));
        Assert.Empty(engine.Tick(T0.AddMinutes(90)));
    }

    [Fact]
    public void An_inventory_equal_by_content_is_not_progress_and_the_name_case_does_not_matter()
    {
        var engine = Started();
        CharacterRecord Rec(string name, int qty) => new(name, T0, Inventory: new SkuaInventory("Hero", ["inv"], [new SkuaItem("Sword", qty, "inv")]));

        engine.NoteProgress([Rec("Hero", 1)], T0.AddMinutes(3));
        engine.NoteProgress([Rec("HERO", 1)], T0.AddMinutes(8));   // a new but equal snapshot

        Assert.Single(engine.Tick(T0.AddMinutes(10)));

        engine.NoteProgress([Rec("hero", 2)], T0.AddMinutes(11));   // really different, other case
        Assert.Empty(engine.Tick(T0.AddMinutes(20)));
    }

    [Fact]
    public void A_character_is_watched_under_one_key_whatever_the_case()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status("Hero", profile: Profile()))), T0);
        engine.ProcessSkua(View(Inst("a", "Hero", Status("HERO", profile: Profile()))), T0.AddMinutes(1));

        Assert.Single(engine.Tick(T0.AddMinutes(10)));
    }

    [Fact]
    public void Two_characters_are_watched_apart()
    {
        var engine = new NoticeEngine();
        engine.ProcessSkua(View(Inst("a", "Hero", Status(profile: Profile())), Inst("b", "Zed", Status("Zed", profile: Profile()))), T0);
        engine.ProcessSkua(View(Inst("a", "Hero", Status(profile: Profile())), Inst("b", "Zed", Status("Zed", profile: Profile(xp: 9)))), T0.AddMinutes(8));

        var notice = Assert.Single(engine.Tick(T0.AddMinutes(10)));

        Assert.Equal("Hero", notice.Character);
    }
}
