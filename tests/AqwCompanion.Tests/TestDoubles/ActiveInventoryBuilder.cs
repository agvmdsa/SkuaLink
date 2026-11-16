using AqwCompanion.App.Inventory;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Builds an empty active inventory: no character is selected, so it has no items.</summary>
public static class ActiveInventoryBuilder
{
    public static IActiveInventory Empty() => new FakeActiveInventory();
}
