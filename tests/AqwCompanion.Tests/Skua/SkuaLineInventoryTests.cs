using System.Text;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Skua;

public class SkuaLineInventoryTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Skua", "fixtures", name)).Trim();

    private static InventoryMessage Sample() => new(new SkuaInventory("Hero", ["inv", "house"],
    [
        new SkuaItem("Blinding Light of Destiny", 1, "inv"),
        new SkuaItem("Dragon Scale", 250, "inv"),
        new SkuaItem("Gold Statue", 1, "house"),
    ]));

    [Fact]
    public void An_inventory_matches_its_fixture_both_ways()
    {
        Assert.Equal(Fixture("inventory.json"), SkuaLine.ToLine(Sample()));
        Assert.Equal(Sample(), SkuaLine.Parse(Fixture("inventory.json")));
    }

    [Fact]
    public void A_truncated_inventory_keeps_its_flag_both_ways()
    {
        var parsed = Assert.IsType<InventoryMessage>(SkuaLine.Parse(Fixture("inventory-truncated.json")));
        Assert.True(parsed.Inventory.Truncated);
        Assert.Equal(Fixture("inventory-truncated.json"), SkuaLine.ToLine(parsed));
    }

    [Fact]
    public void An_empty_container_that_was_read_travels_as_read_with_no_items()
    {
        var empty = new InventoryMessage(new SkuaInventory("Hero", ["inv", "house"], [new SkuaItem("Sword", 1, "inv")]));
        var parsed = Assert.IsType<InventoryMessage>(SkuaLine.Parse(SkuaLine.ToLine(empty)));
        Assert.Contains("house", parsed.Inventory.Read);
        Assert.DoesNotContain(parsed.Inventory.Items, i => i.Where == "house");
    }

    [Fact]
    public void Entries_of_a_container_not_read_or_unknown_are_dropped_one_by_one()
    {
        var line = """{"v":1,"type":"inventory","read":["inv","pocket"],"items":[{"name":"A","qty":1,"where":"inv"},{"name":"B","qty":1,"where":"bank"},{"name":"C","qty":1,"where":"pocket"},{"name":"","qty":1,"where":"inv"},{"name":"D","qty":-2,"where":"inv"}]}""";
        var parsed = Assert.IsType<InventoryMessage>(SkuaLine.Parse(line));
        Assert.Equal(["inv"], parsed.Inventory.Read);
        Assert.Equal([new SkuaItem("A", 1, "inv")], parsed.Inventory.Items);
        Assert.Null(parsed.Inventory.Character);
    }

    [Fact]
    public void Without_read_or_items_the_line_is_refused()
    {
        Assert.Null(SkuaLine.Parse("""{"v":1,"type":"inventory","items":[]}"""));
        Assert.Null(SkuaLine.Parse("""{"v":1,"type":"inventory","read":[]}"""));
    }

    [Fact]
    public void At_most_5000_entries_per_container_are_written_and_read_and_the_cut_is_flagged()
    {
        var many = Enumerable.Range(0, SkuaProtocol.MaxInventoryItems + 10).Select(i => new SkuaItem($"I{i}", 1, "inv"))
            .Append(new SkuaItem("Statue", 1, "house")).ToList();
        var parsed = Assert.IsType<InventoryMessage>(SkuaLine.Parse(SkuaLine.ToLine(new InventoryMessage(new SkuaInventory("Hero", ["inv", "house"], many)))));
        Assert.Equal(SkuaProtocol.MaxInventoryItems, parsed.Inventory.Items.Count(i => i.Where == "inv"));
        Assert.Contains(new SkuaItem("Statue", 1, "house"), parsed.Inventory.Items);
        Assert.True(parsed.Inventory.Truncated);
    }

    [Fact]
    public void A_line_over_one_megabyte_is_cut_from_the_end_and_flagged()
    {
        var name = new string('n', SkuaProtocol.MaxTextLength);
        var items = Enumerable.Range(0, 4000).Select(i => new SkuaItem(name, i, i < 2000 ? "inv" : "bank")).ToList();
        var line = SkuaLine.ToLine(new InventoryMessage(new SkuaInventory("Hero", ["inv", "bank"], items)));
        Assert.True(Encoding.UTF8.GetByteCount(line) <= SkuaProtocol.MaxInventoryLineBytes);
        var parsed = Assert.IsType<InventoryMessage>(SkuaLine.Parse(line));
        Assert.True(parsed.Inventory.Truncated);
        Assert.Equal(2000, parsed.Inventory.Items.Count(i => i.Where == "inv"));
    }

    [Fact]
    public void The_inventory_cap_fits_the_app_reader_and_a_bigger_line_is_refused()
    {
        Assert.True(SkuaProtocol.MaxInventoryLineBytes <= SkuaProtocol.MaxResultLineBytes);
        var huge = """{"v":1,"type":"inventory","read":["inv"],"items":[{"name":"x","qty":1,"where":"inv","pad":""" + "\"" + new string('p', SkuaProtocol.MaxInventoryLineBytes) + "\"}]}";
        Assert.Null(SkuaLine.Parse(huge));
    }

    [Fact]
    public void Inventories_compare_by_content()
    {
        Assert.Equal(Sample(), Sample());
        Assert.Equal(Sample().GetHashCode(), Sample().GetHashCode());
        Assert.NotEqual(Sample().Inventory, Sample().Inventory with { Truncated = true });
    }

    [Fact]
    public void An_escaped_lone_surrogate_in_one_item_name_drops_only_that_item()
    {
        var line = """{"v":1,"type":"inventory","read":["inv"],"items":[{"name":"\ud800","qty":1,"where":"inv"},{"name":"Ok","qty":2,"where":"inv"}]}""";
        var parsed = Assert.IsType<InventoryMessage>(SkuaLine.Parse(line));
        Assert.Equal([new SkuaItem("Ok", 2, "inv")], parsed.Inventory.Items);
    }
}
