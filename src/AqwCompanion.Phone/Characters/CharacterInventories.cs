using System.Runtime.CompilerServices;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Characters;

/// <summary>The inventory shape the Hard Farms matching reads, built once per snapshot instance (bag, bank and house together).</summary>
public static class CharacterInventories
{
    private static readonly ConditionalWeakTable<SkuaInventory, CharacterInventory> Cache = new();

    public static CharacterInventory For(SkuaInventory inventory) => Cache.GetValue(inventory, static i =>
        new CharacterInventory([.. i.Items.Select(item => new InventoryItem(item.Name, CharacterInventory.OtherTypeName, item.Quantity, false, false))]));
}
