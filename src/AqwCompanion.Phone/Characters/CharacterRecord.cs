using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Characters;

/// <summary>
/// What SkuaLink last knew of one character: its numbers, reputation and inventory, each with the time it was read.
/// <paramref name="ProfileChangedAtUtc"/> and <paramref name="ProfileChanges"/> count profile changes in this session only and
/// are never stored.
/// </summary>
public sealed record CharacterRecord(
    string Name,
    DateTimeOffset LastSeenUtc,
    SkuaProfile? Profile = null,
    DateTimeOffset? ProfileAtUtc = null,
    IReadOnlyList<SkuaFaction>? Factions = null,
    DateTimeOffset? FactionsAtUtc = null,
    SkuaInventory? Inventory = null,
    DateTimeOffset? InventoryAtUtc = null,
    DateTimeOffset? ProfileChangedAtUtc = null,
    int ProfileChanges = 0);
