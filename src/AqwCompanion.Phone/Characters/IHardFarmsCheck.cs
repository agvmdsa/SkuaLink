using AqwCompanion.Core.Feed;
using AqwCompanion.Core.Inventory;

namespace AqwCompanion.Phone.Characters;

/// <summary>Matches the inventory of any character against the loaded Hard Farms list. Safe from any thread.</summary>
public interface IHardFarmsCheck
{
    /// <summary>Raised when the list or its item types were applied or forgotten (every answer may change).</summary>
    event EventHandler? Changed;

    /// <summary>Owned of total and the missing names (capped), or null while no list is loaded.</summary>
    FeedHardFarms? Check(CharacterInventory inventory);
}
