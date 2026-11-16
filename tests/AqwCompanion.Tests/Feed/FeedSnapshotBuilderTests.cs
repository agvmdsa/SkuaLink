using System.Text.Json.Nodes;
using AqwCompanion.App.Feed;
using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Feed;

public class FeedSnapshotBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static CharacterRecord Alpha() => new(
        "Alpha Hero", T0.AddMinutes(5), new SkuaProfile(60, 340, 5000, 1200, 777, 10, true, "Void Highlord", 31, 99), T0,
        [new SkuaFaction(1, "Nation", 3, 5000, 1400, 6400), new SkuaFaction(2, "Good", 10, 302500, 0, 0)], T0,
        ProfileChangedAtUtc: T0.AddMinutes(5), ProfileChanges: 3);

    private static CharacterRecord Beta() => new("Beta Hero", T0.AddHours(-1), new SkuaProfile(12, 6, 7, 5, 8, 1, false), T0.AddHours(-1));

    private static SkuaHubView LiveAlpha() => new([FakeSkua.Instance("a", "Alpha Hero", status: new SkuaStatus(true, "Alpha Hero", null, null, null, false, null))]);

    [Fact]
    public void The_snapshot_from_skua_matches_the_golden_contract_1_text()
    {
        var snapshot = FeedSnapshotBuilder.Build([Beta(), Alpha()], LiveAlpha(), "alpha hero", new HardFarmsSummary(4, 8, ["Cape of Chaos", "Nation Hoodie"]));
        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Feed", "snapshot-from-skua.json"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(golden), JsonNode.Parse(FeedJson.Serialize(snapshot))));
    }

    [Fact]
    public void The_contract_number_stays_one()
    {
        Assert.Equal(1, FeedSnapshotBuilder.Build([], SkuaHubView.Empty, null, null).Contract);
    }

    [Fact]
    public void A_character_without_numbers_is_left_out_and_nothing_selected_has_no_factions_or_hard_farms()
    {
        var snapshot = FeedSnapshotBuilder.Build([new CharacterRecord("Old Plugin", T0)], SkuaHubView.Empty, null, new HardFarmsSummary(1, 2, []));
        Assert.Empty(snapshot.Characters);
        Assert.Empty(snapshot.Factions);
        Assert.Null(snapshot.HardFarms);
        Assert.Equal(new FeedCapture("Skua not connected.", false), snapshot.Capture);
    }

    [Theory]
    [InlineData(0, "Skua not connected.")]
    [InlineData(1, "1 Skua connected.")]
    [InlineData(3, "3 Skua connected.")]
    public void The_skua_state_text_counts_connected_skua(int connected, string text)
    {
        Assert.Equal(text, FeedSnapshotBuilder.SkuaStateText(connected));
    }
}
