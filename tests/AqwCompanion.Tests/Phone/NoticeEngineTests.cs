using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Notices;

namespace AqwCompanion.Tests.Phone;

public class NoticeEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static FeedCharacter Hero(string name, bool live = true, DateTimeOffset? last = null) =>
        new(name, 60, live, T0, last, 1, new FeedFigures(1, 2, 3, 4, 5, 6), null);

    private static FeedSnapshot Snap(
        string? selected = "Alpha",
        bool listening = true,
        IReadOnlyList<FeedCharacter>? characters = null,
        IReadOnlyList<FeedFaction>? factions = null,
        FeedHardFarms? hardFarms = null) =>
        new(1, new FeedCapture("x", listening), selected, characters ?? [Hero("Alpha", last: T0)], factions ?? [], hardFarms);

    private static FeedFaction Faction(string name, int rank) => new(name, rank, 0, 0, 0, false);

    [Fact]
    public void The_first_snapshot_is_only_a_baseline()
    {
        var engine = new NoticeEngine();

        var notices = engine.Process(Snap(factions: [Faction("Yokai", 8)], hardFarms: new FeedHardFarms(1, 3, ["A", "B"])), T0);

        Assert.Empty(notices);
    }

    [Fact]
    public void A_higher_rank_for_the_same_selected_character_is_a_rank_up()
    {
        var engine = new NoticeEngine();
        engine.Process(Snap(factions: [Faction("Yokai", 8), Faction("Good", 10)]), T0);

        var notices = engine.Process(Snap(factions: [Faction("Yokai", 9), Faction("Good", 10)]), T0.AddSeconds(5));

        var n = Assert.Single(notices);
        Assert.Equal(NoticeKind.RankUp, n.Kind);
        Assert.Equal("Alpha", n.Character);
        Assert.Equal("rank:Alpha:Yokai:9", n.Id);
        Assert.Contains("Yokai", n.Body);
        Assert.Contains("9", n.Body);
    }

    [Fact]
    public void Switching_the_selected_character_gives_no_rank_notices()
    {
        var engine = new NoticeEngine();
        engine.Process(Snap(selected: "Alpha", factions: [Faction("Yokai", 3)]), T0);

        var notices = engine.Process(Snap(selected: "Beta", factions: [Faction("Yokai", 9)]), T0.AddSeconds(5));

        Assert.Empty(notices);
    }

    [Fact]
    public void An_entry_that_left_the_missing_list_while_owned_grew_is_a_hard_farm_drop()
    {
        var engine = new NoticeEngine();
        engine.Process(Snap(hardFarms: new FeedHardFarms(1, 3, ["Cape", "Blade"])), T0);

        var notices = engine.Process(Snap(hardFarms: new FeedHardFarms(2, 3, ["Blade"])), T0.AddSeconds(5));

        var n = Assert.Single(notices);
        Assert.Equal(NoticeKind.HardFarmDrop, n.Kind);
        Assert.Equal("hf:Alpha:Cape", n.Id);
        Assert.Contains("Cape", n.Body);
    }

    [Fact]
    public void A_different_total_means_the_list_was_imported_again_and_gives_no_drops()
    {
        var engine = new NoticeEngine();
        engine.Process(Snap(hardFarms: new FeedHardFarms(1, 3, ["Cape", "Blade"])), T0);

        var notices = engine.Process(Snap(hardFarms: new FeedHardFarms(3, 5, [])), T0.AddSeconds(5));

        Assert.Empty(notices);
    }

    [Fact]
    public void A_name_that_was_not_there_before_is_a_new_login()
    {
        var engine = new NoticeEngine();
        engine.Process(Snap(characters: [Hero("Alpha")]), T0);

        var notices = engine.Process(Snap(characters: [Hero("Alpha"), Hero("Beta")]), T0.AddSeconds(5));

        var n = Assert.Single(notices);
        Assert.Equal(NoticeKind.NewLogin, n.Kind);
        Assert.Equal("Beta", n.Character);
    }

    [Fact]
    public void A_change_of_the_skua_connection_in_the_feed_raises_no_capture_notice()
    {
        var engine = new NoticeEngine();
        engine.Process(Snap(listening: true), T0);

        Assert.Empty(engine.Process(Snap(listening: false), T0.AddSeconds(5)));
    }

    [Fact]
    public void A_stalled_feed_character_no_longer_raises_ActivityStopped()
    {
        var engine = new NoticeEngine();
        engine.Process(Snap(characters: [Hero("Alpha", live: true, last: T0)]), T0);

        Assert.Empty(engine.Tick(T0.AddHours(5)));
        Assert.Empty(engine.Process(Snap(characters: [Hero("Alpha", live: true, last: T0)]), T0.AddHours(5)));
    }

    [Fact]
    public void The_same_id_is_admitted_only_once()
    {
        var engine = new NoticeEngine();
        engine.Process(Snap(factions: [Faction("Yokai", 8)]), T0);
        Assert.Single(engine.Process(Snap(factions: [Faction("Yokai", 9)]), T0.AddSeconds(1)));

        engine.Process(Snap(factions: [Faction("Yokai", 8)]), T0.AddSeconds(2));
        var again = engine.Process(Snap(factions: [Faction("Yokai", 9)]), T0.AddSeconds(3));

        Assert.Empty(again);
    }

    [Fact]
    public void No_more_than_six_notices_per_character_per_minute()
    {
        var engine = new NoticeEngine();
        var many = Enumerable.Range(0, 10).Select(i => $"Entry{i}").ToList();
        engine.Process(Snap(hardFarms: new FeedHardFarms(0, 10, many)), T0);

        var notices = engine.Process(Snap(hardFarms: new FeedHardFarms(10, 10, [])), T0.AddSeconds(5));

        Assert.Equal(6, notices.Count);
    }

    [Fact]
    public void Reset_makes_the_next_snapshot_a_baseline_again()
    {
        var engine = new NoticeEngine();
        engine.Process(Snap(factions: [Faction("Yokai", 8)]), T0);

        engine.Reset();
        var notices = engine.Process(Snap(factions: [Faction("Yokai", 9)]), T0.AddSeconds(5));

        Assert.Empty(notices);
        Assert.Empty(engine.Tick(T0.AddSeconds(6)));
    }

    [Fact]
    public void Reset_forgets_the_remembered_ids()
    {
        var engine = new NoticeEngine();
        engine.Process(Snap(factions: [Faction("Yokai", 8)]), T0);
        Assert.Single(engine.Process(Snap(factions: [Faction("Yokai", 9)]), T0.AddSeconds(5)));

        engine.Reset();
        engine.Process(Snap(factions: [Faction("Yokai", 8)]), T0.AddSeconds(10));
        var again = engine.Process(Snap(factions: [Faction("Yokai", 9)]), T0.AddSeconds(15));

        var n = Assert.Single(again);
        Assert.Equal("rank:Alpha:Yokai:9", n.Id);
    }
}
