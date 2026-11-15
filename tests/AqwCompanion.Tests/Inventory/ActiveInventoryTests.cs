using AqwCompanion.App.Characters;
using AqwCompanion.App.Inventory;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Inventory;

public class ActiveInventoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeSkua _skua = new();
    private readonly CharacterBook _book = new(new MemoryCharacterBookStore(), () => T0);

    [Fact]
    public void The_inventory_of_the_selected_character_is_current_and_stays_the_same_instance()
    {
        _book.OnStatus(new SkuaStatus(true, "Hero", null, null, null, false, null));
        _book.OnInventory("Hero", new SkuaInventory("Hero", ["inv"], [new SkuaItem("Sword", 1, "inv")]));
        var active = new ActiveInventory(_book, new CharacterSelection(_book, _skua));
        Assert.True(active.Current!.Has("Sword"));
        Assert.Same(active.Current, active.Current);
    }

    [Fact]
    public void Changed_is_raised_on_the_ui_callback_only_when_the_inventory_instance_changes()
    {
        var posted = 0;
        var active = new ActiveInventory(_book, new CharacterSelection(_book, _skua), a => { posted++; a(); });
        var raised = 0;
        active.Changed += (_, _) => raised++;
        _book.OnStatus(new SkuaStatus(true, "Hero", null, null, null, false, null));
        _book.OnInventory("Hero", new SkuaInventory("Hero", ["inv"], [new SkuaItem("Sword", 1, "inv")]));
        _book.OnInventory("Hero", new SkuaInventory("Hero", ["inv"], [new SkuaItem("Sword", 1, "inv")]));
        Assert.Equal(1, raised);
        Assert.Equal(1, posted);
    }

    [Fact]
    public void Without_a_selected_character_or_inventory_there_is_none()
    {
        Assert.Null(new ActiveInventory(_book, new CharacterSelection(_book, _skua)).Current);
    }
}
