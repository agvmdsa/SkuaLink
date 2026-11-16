using AqwCompanion.Phone.Characters;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Skua;

public class SkuaInventoryReadTests
{
    private static List<(string? Name, int Quantity)> Items(params string[] names) => [.. names.Select(n => ((string?)n, 1))];

    [Fact]
    public void Bag_and_house_are_always_read_together_and_the_bank_only_when_it_has_items()
    {
        var a = SkuaInventoryRead.Build("Hero", Items("Sword"), [], Items("Statue"))!;
        Assert.Equal(["inv", "house"], a.Read);
        var b = SkuaInventoryRead.Build("Hero", Items("Sword"), Items("Old Axe"), [])!;
        Assert.Equal(["inv", "house", "bank"], b.Read);
        Assert.Equal([new SkuaItem("Sword", 1, "inv"), new SkuaItem("Old Axe", 1, "bank")], b.Items);
    }

    [Fact]
    public void An_empty_or_missing_bag_means_nothing_to_report()
    {
        Assert.Null(SkuaInventoryRead.Build("Hero", [], Items("Axe"), Items("Statue")));
        Assert.Null(SkuaInventoryRead.Build("Hero", null, null, null));
    }

    [Fact]
    public void Blank_names_are_skipped_names_trimmed_and_negative_quantities_zero()
    {
        var inv = SkuaInventoryRead.Build(" Hero ", [("  Sword ", -3), ("", 5), (null, 1)], null, null)!;
        Assert.Equal("Hero", inv.Character);
        Assert.Equal([new SkuaItem("Sword", 0, "inv")], inv.Items);
    }

    [Fact]
    public void Each_container_is_capped_at_5000_and_the_cut_is_flagged()
    {
        var bag = Enumerable.Range(0, 5001).Select(i => ((string?)$"I{i}", 1)).ToList();
        var inv = SkuaInventoryRead.Build("Hero", bag, null, null)!;
        Assert.Equal(5000, inv.Items.Count);
        Assert.True(inv.Truncated);
    }

    [Fact]
    public void The_equipped_class_is_the_equipped_item_of_category_class()
    {
        var facts = SkuaInventoryRead.EquippedClass(
        [
            (1, "Sword", 1, true, "Sword"),
            (2, "Ninja", 2400, false, "Class"),
            (31, " Void Highlord ", 302500, true, "class"),
        ]);
        Assert.Equal(new SkuaClassFacts(31, "Void Highlord", 302500), facts);
        Assert.Null(SkuaInventoryRead.EquippedClass([(2, "Ninja", 2400, false, "Class")]));
    }

    [Fact]
    public void A_bank_equal_to_the_stale_one_is_not_read_and_a_different_one_is()
    {
        var stale = Items("Old Axe");
        var same = SkuaInventoryRead.Build("Hero", Items("Sword"), [(" Old Axe ", 1)], [], stale)!;
        Assert.Equal(["inv", "house"], same.Read);
        Assert.DoesNotContain(same.Items, i => i.Where == "bank");
        var other = SkuaInventoryRead.Build("Hero", Items("Sword"), Items("Old Axe", "Shield"), [], stale)!;
        Assert.Equal(["inv", "house", "bank"], other.Read);
    }

    [Fact]
    public void The_bank_read_for_another_character_is_stale_until_one_for_this_character_arrives()
    {
        var last = Items("Old Axe");
        Assert.Null(SkuaInventoryRead.StaleBank("Hero", "hero", last));
        Assert.Same(last, SkuaInventoryRead.StaleBank("Hero", "Alt", last));
        Assert.Null(SkuaInventoryRead.StaleBank(null, "Alt", null));
    }

    [Fact]
    public void A_bank_counts_as_read_only_when_it_has_items_and_is_not_the_stale_one()
    {
        Assert.False(SkuaInventoryRead.BankRead([], null));
        Assert.False(SkuaInventoryRead.BankRead(null, null));
        Assert.True(SkuaInventoryRead.BankRead(Items("Axe"), null));
        Assert.False(SkuaInventoryRead.BankRead(Items("Axe"), Items("Axe")));
        Assert.True(SkuaInventoryRead.BankRead(Items("Axe"), Items("Bow")));
    }

    [Fact]
    public void A_house_that_was_not_read_is_not_listed_as_read_and_an_empty_one_is()
    {
        var missing = SkuaInventoryRead.Build("Hero", Items("Sword"), null, null)!;
        Assert.Equal(["inv"], missing.Read);
        var empty = SkuaInventoryRead.Build("Hero", Items("Sword"), null, [])!;
        Assert.Equal(["inv", "house"], empty.Read);
    }

    [Fact]
    public void A_house_that_was_not_read_keeps_the_remembered_house_in_the_book()
    {
        var at = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        using var book = new CharacterBook(new MemoryCharacterBookStore(), () => at);
        book.OnInventory("Hero", SkuaInventoryRead.Build("Hero", Items("Sword"), null, Items("Statue"))!);

        book.OnInventory("Hero", SkuaInventoryRead.Build("Hero", Items("Sword", "Shield"), null, null)!);

        var inventory = book.Find("Hero")!.Inventory!;
        Assert.Contains(new SkuaItem("Statue", 1, "house"), inventory.Items);
        Assert.Contains(new SkuaItem("Shield", 1, "inv"), inventory.Items);
    }
}
