using AqwCompanion.Core.Inventory;

namespace AqwCompanion.App.HardFarms;

/// <summary>One entry of the Hard Farms list as the ownership matching needs it: its page, display name and wiki name (if known).</summary>
public sealed record HardFarmEntry(string Slug, string Name, string? WikiName);

/// <summary>How many entries of the Hard Farms list an inventory owns, by the same name matching the Hard Farms tab uses.</summary>
public static class HardFarmsOwnership
{
    /// <summary>Owned of total and the names not owned in list order; null without entries or without an inventory (unknown then).</summary>
    public static HardFarmsSummary? Summarize(IReadOnlyList<HardFarmEntry> entries, CharacterInventory? inventory)
    {
        if (entries.Count == 0 || inventory is null)
        {
            return null;
        }

        var owned = 0;
        var missing = new List<string>();
        foreach (var entry in entries)
        {
            if (OwnershipMatcher.Match(entry.Slug, entry.WikiName, inventory) == Ownership.Owned)
            {
                owned++;
            }
            else
            {
                missing.Add(entry.Name);
            }
        }

        return new HardFarmsSummary(owned, entries.Count, missing);
    }
}
