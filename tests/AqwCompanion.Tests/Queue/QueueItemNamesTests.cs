using AqwCompanion.App.Queue;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Queue;

public class QueueItemNamesTests
{
    private static CharacterRecord Record(params SkuaItem[] items) =>
        new("Hero", DateTimeOffset.UnixEpoch, Inventory: new SkuaInventory("Hero", [SkuaProtocol.ContainerInventory], items));

    private static SkuaItem Inv(string name) => new(name, 1, SkuaProtocol.ContainerInventory);

    [Fact]
    public void No_record_or_no_inventory_gives_nothing()
    {
        Assert.Empty(QueueItemNames.From(null));
        Assert.Empty(QueueItemNames.From(new CharacterRecord("Hero", DateTimeOffset.UnixEpoch)));
    }

    [Fact]
    public void Only_inventory_items_count()
    {
        var names = QueueItemNames.From(Record(Inv("Blood Orb"), new SkuaItem("Bank Thing", 1, "bank"), new SkuaItem("House Thing", 1, "house")));

        Assert.Equal(["Blood Orb"], names);
    }

    [Fact]
    public void Names_are_distinct_ignoring_case_and_sorted_ignoring_case()
    {
        var names = QueueItemNames.From(Record(Inv("zinc"), Inv("Apple"), Inv("APPLE"), Inv("banana")));

        Assert.Equal(["Apple", "banana", "zinc"], names);
    }

    [Fact]
    public void The_list_is_capped_at_500()
    {
        var names = QueueItemNames.From(Record([.. Enumerable.Range(0, 800).Select(i => Inv("Item " + i.ToString("D4")))]));

        Assert.Equal(500, names.Count);
    }
}
