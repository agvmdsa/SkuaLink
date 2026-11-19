using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaBoostItemsTests
{
    [Theory]
    [InlineData("GOLD Boost! (1 hr)", "gold")]
    [InlineData("CLASS Boost! (1 hr)", "class")]
    [InlineData("Daily Login Class Boost (20 min)", "class")]
    [InlineData("XP Boost (1 hr)", "xp")]
    [InlineData("Experience Boost", "xp")]
    [InlineData("Reputation Boost", "rep")]
    [InlineData("REP Boost", "rep")]
    [InlineData("Gold and Class Boost", "unknown")]
    [InlineData("Mystery Boost", "unknown")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    [InlineData("   ", "unknown")]
    public void TypeOf_deduces_the_type_from_the_name(string? name, string expected) =>
        Assert.Equal(expected, SkuaBoostItems.TypeOf(name));

    [Fact]
    public void Build_keeps_only_serveruse_items_named_boost_with_a_quantity_and_an_id()
    {
        var items = SkuaBoostItems.Build(
        [
            (17439, "GOLD Boost! (1 hr)", 33, true),
            (22447, "Daily Login Class Boost (20 min)", 61, true),
            (1, "Potion", 3, true),
            (2, "Boost Dust", 3, false),
            (0, "GOLD Boost", 1, true),
            (5, "GOLD Boost", 0, true),
            (6, null, 1, true),
        ]);

        Assert.Equal([17439, 22447], items.Select(i => i.ItemId));
        Assert.Equal(new SkuaBoostItem(17439, "GOLD Boost! (1 hr)", "gold", 33), items[0]);
        Assert.Equal(new SkuaBoostItem(22447, "Daily Login Class Boost (20 min)", "class", 61), items[1]);
    }

    [Fact]
    public void Build_orders_by_type_then_name_and_caps_at_twelve_and_de_duplicates_ids()
    {
        var bag = new List<(int, string?, int, bool)>();
        var id = 100;
        foreach (var word in new[] { "XP", "Mystery", "REP", "CLASS", "GOLD" })
        {
            foreach (var letter in new[] { "C", "A", "B" })
            {
                bag.Add((id++, $"{word} Boost {letter}", 1, true));
            }
        }

        bag.Add((100, "GOLD Boost duplicate", 1, true));

        var items = SkuaBoostItems.Build(bag);

        Assert.Equal(SkuaProtocol.MaxBoostItems, items.Count);
        Assert.Equal(
            ["gold", "gold", "gold", "class", "class", "class", "rep", "rep", "rep", "xp", "xp", "xp"],
            items.Select(i => i.Type));
        Assert.Equal(["GOLD Boost A", "GOLD Boost B", "GOLD Boost C"], items.Take(3).Select(i => i.Name));
        Assert.Single(items, i => i.ItemId == 100);
        Assert.Equal("XP Boost C", items.Single(i => i.ItemId == 100).Name);
    }

    [Fact]
    public void Build_trims_and_cuts_names_to_100()
    {
        var items = SkuaBoostItems.Build([(1, "  GOLD Boost " + new string('x', 200) + "  ", 1, true)]);

        Assert.Equal(SkuaProtocol.MaxBoostNameLength, items.Single().Name.Length);
        Assert.StartsWith("GOLD Boost x", items.Single().Name);
    }

    [Fact]
    public void Build_does_not_split_a_surrogate_pair_at_the_cut()
    {
        var name = "GOLD Boost " + new string('x', SkuaProtocol.MaxBoostNameLength - 12) + "\U0001F600tail";
        Assert.True(char.IsHighSurrogate(name[SkuaProtocol.MaxBoostNameLength - 1]));

        var cut = SkuaBoostItems.Build([(1, name, 1, true)]).Single().Name;

        Assert.False(char.IsHighSurrogate(cut[^1]));
        Assert.True(cut.Length <= SkuaProtocol.MaxBoostNameLength);
    }
}
