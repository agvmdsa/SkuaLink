using AqwCompanion.Phone.Characters;
using AqwCompanion.Skua;

namespace AqwCompanion.App.Queue;

/// <summary>The item names the editor suggests: the distinct inventory-container names of the character's last snapshot.</summary>
public static class QueueItemNames
{
    public const int Max = 500;

    public static IReadOnlyList<string> From(CharacterRecord? record) =>
        record?.Inventory is { } inventory
            ? [.. inventory.Items
                .Where(i => i.Where == SkuaProtocol.ContainerInventory && !string.IsNullOrWhiteSpace(i.Name))
                .Select(i => i.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Take(Max)]
            : [];
}
