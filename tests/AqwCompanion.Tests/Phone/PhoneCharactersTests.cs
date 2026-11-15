using AqwCompanion.Core.Feed;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class PhoneCharactersTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly SkuaProfile Stored = new(59, 1, 2, 3, 4, 9, false);
    private static readonly SkuaProfile LiveProfile = new(60, 1, 2, 3, 4, 10, true);

    private sealed class FixedCheck(FeedHardFarms? answer) : IHardFarmsCheck
    {
        public event EventHandler? Changed { add { } remove { } }

        public FeedHardFarms? Check(CharacterInventory inventory) => answer;
    }

    private static SkuaInstanceView Playing(string id, string character, SkuaProfile? profile = null, IReadOnlyList<SkuaFaction>? factions = null, bool disconnected = false) =>
        FakeSkua.Instance(id, character, disconnected, new SkuaStatus(true, character, null, null, null, false, null, [], factions ?? [], null, null, false, profile));

    [Fact]
    public void A_live_character_takes_the_live_profile_and_factions()
    {
        var record = new CharacterRecord("Hero", T0, Stored, T0, [new SkuaFaction(1, "Old", 1, 0, 0, 1)], T0);
        var row = Assert.Single(PhoneCharacters.Build([record], new SkuaHubView([Playing("a", "hero", LiveProfile, [new SkuaFaction(2, "Nation", 3, 1, 1, 2)])]), null));
        Assert.True(row.Live);
        Assert.Equal("a", row.InstanceId);
        Assert.Equal(LiveProfile, row.Profile);
        Assert.True(row.FactionsLive);
        Assert.Equal("Nation", Assert.Single(row.Factions!).Name);
    }

    [Fact]
    public void A_live_character_with_an_empty_faction_read_or_no_profile_keeps_the_stored_ones()
    {
        var record = new CharacterRecord("Hero", T0, Stored, T0, [new SkuaFaction(1, "Old", 1, 0, 0, 1)], T0);
        var row = PhoneCharacters.Build([record], new SkuaHubView([Playing("a", "Hero")]), null).Single();
        Assert.Equal(Stored, row.Profile);
        Assert.False(row.FactionsLive);
        Assert.Equal("Old", row.Factions!.Single().Name);
    }

    [Fact]
    public void A_remembered_character_points_to_its_last_skua_and_is_not_live()
    {
        var row = PhoneCharacters.Build([new CharacterRecord("Hero", T0, Stored)], new SkuaHubView([Playing("a", "Hero", disconnected: true)]), null).Single();
        Assert.False(row.Live);
        Assert.Equal("a", row.InstanceId);
        Assert.Equal(T0, row.LastSeenUtc);
    }

    [Fact]
    public void A_skua_without_a_logged_in_character_is_a_row_of_its_own_at_the_end()
    {
        var rows = PhoneCharacters.Build([new CharacterRecord("Hero", T0)], new SkuaHubView([FakeSkua.Instance("b", "Skua 1")]), null);
        Assert.Equal(["Hero", "Skua 1"], rows.Select(r => r.Name));
        Assert.False(rows[1].HasCharacter);
        Assert.Equal("b", rows[1].InstanceId);
    }

    [Fact]
    public void Live_characters_come_first_then_the_most_recently_seen()
    {
        var rows = PhoneCharacters.Build(
            [new CharacterRecord("Alt", T0.AddHours(-2)), new CharacterRecord("Old", T0.AddHours(-1)), new CharacterRecord("Zed", T0)],
            new SkuaHubView([Playing("z", "Zed")]), null);
        Assert.Equal(["Zed", "Old", "Alt"], rows.Select(r => r.Name));
    }

    [Theory]
    [InlineData(false, false, PhoneHardFarmsState.NoInventory)]
    [InlineData(true, false, PhoneHardFarmsState.NoList)]
    [InlineData(true, true, PhoneHardFarmsState.Ready)]
    public void The_hard_farms_state_says_what_is_missing(bool hasInventory, bool hasList, PhoneHardFarmsState expected)
    {
        var inventory = hasInventory ? new SkuaInventory("Hero", ["inv"], [new SkuaItem("Sword", 1, "inv")], Truncated: true) : null;
        var record = new CharacterRecord("Hero", T0, Inventory: inventory, InventoryAtUtc: hasInventory ? T0 : null);
        var row = PhoneCharacters.Build([record], SkuaHubView.Empty, new FixedCheck(hasList ? new FeedHardFarms(1, 2, ["X"]) : null)).Single();
        Assert.Equal(expected, row.HardFarmsState);
        Assert.Equal(expected == PhoneHardFarmsState.Ready, row.HardFarms is not null);
        Assert.Equal(hasInventory, row.InventoryTruncated);
    }

    [Fact]
    public void A_character_row_serializes_with_camel_case_names_explicit_nulls_and_string_enums()
    {
        var snapshot = new FeedSnapshot(1, new FeedCapture("Listening.", true), null, [], [], null);
        var row = new PhoneCharacter("Hero", true, "a", false, T0, null, null, false, PhoneHardFarmsState.Ready, new FeedHardFarms(1, 2, ["X"]), T0, true);

        var json = PhoneJson.Serialize(new PhoneState(snapshot, [], Characters: [row]));

        Assert.Contains(
            "\"characters\":[{\"name\":\"Hero\",\"hasCharacter\":true,\"instanceId\":\"a\",\"live\":false,\"lastSeenUtc\":\"2026-11-15T12:00:00+00:00\","
            + "\"profile\":null,\"factions\":null,\"factionsLive\":false,\"hardFarmsState\":\"Ready\",\"hardFarms\":{\"owned\":1,\"total\":2,\"missing\":[\"X\"]},"
            + "\"inventoryAtUtc\":\"2026-11-15T12:00:00+00:00\",\"inventoryTruncated\":true}]",
            json);
    }
}
