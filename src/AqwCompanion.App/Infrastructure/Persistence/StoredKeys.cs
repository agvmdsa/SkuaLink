using AqwCompanion.App.Characters;
using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Persistence;

namespace AqwCompanion.App.Infrastructure.Persistence;

/// <summary>The only place stored records are declared: name, version of the stored shape, owning feature. Every key starts at version 1.</summary>
public static class StoredKeys
{
    public static readonly StoredKey<HardFarmStore.ListDto> HardFarmList = new("hardfarms.list", 1, "Hard Farms");

    public static readonly StoredKey<HardFarmStore.IndexesDto> HardFarmIndexes = new("hardfarms.indexes", 1, "Hard Farms");

    public static readonly StoredKey<StoredCharacterBookStore.BookDto> SkuaCharacters = new("skua.characters", 1, "Characters");

    /// <summary>Every key declared above.</summary>
    public static readonly IReadOnlyList<IStoredKey> All =
    [
        HardFarmList, HardFarmIndexes, SkuaCharacters,
    ];

    /// <summary>The Game data record of versions before 0.6 (the removed capture). Never read; left in companion.db as the spec asks (no migration) and removed with everything else by Forget my data, which deletes the database file.</summary>
    public const string LegacyCaptureRecord = "gamedata.characters";

    /// <summary>Names of records no feature uses any more; removed after the first successful save.</summary>
    public static readonly IReadOnlyList<string> Obsolete = ["hardfarms.types", "reputation.data", "identity", "summary", "equipment", "badges", "inventory"];
}
