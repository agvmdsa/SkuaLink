using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Inventory;

namespace AqwCompanion.Tests.HardFarms;

public class HardFarmsOwnershipTests
{
    private static CharacterInventory Owns(params string[] names) => new([.. names.Select(n => new InventoryItem(n, CharacterInventory.OtherTypeName, 1, false, false))]);

    private static readonly IReadOnlyList<HardFarmEntry> Entries =
    [
        new("void-highlord-class", "Void Highlord", "Void Highlord"),
        new("cape-of-chaos", "Cape of Chaos", null),
        new("nation-hoodie", "Nation Hoodie", "Nation Hoodie"),
    ];

    [Fact]
    public void Counts_owned_entries_and_lists_the_missing_names_in_list_order()
    {
        // Without a wiki name the matcher tries the page name only, so the bag name carries no tag here.
        var summary = HardFarmsOwnership.Summarize(Entries, Owns("Void Highlord", "Cape of Chaos"))!;
        Assert.Equal(2, summary.Owned);
        Assert.Equal(3, summary.Total);
        Assert.Equal(["Nation Hoodie"], summary.Missing);
    }

    [Fact]
    public void Without_a_list_or_an_inventory_ownership_is_unknown()
    {
        Assert.Null(HardFarmsOwnership.Summarize([], Owns("x")));
        Assert.Null(HardFarmsOwnership.Summarize(Entries, null));
    }
}
