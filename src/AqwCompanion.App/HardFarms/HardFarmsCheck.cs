using System.Runtime.CompilerServices;
using AqwCompanion.Core.Feed;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Phone.Characters;

namespace AqwCompanion.App.HardFarms;

/// <summary>
/// The Hard Farms answer for any character's inventory over the list the Hard Farms tab loaded. Each answer is cached per
/// inventory instance and list, so an unchanged inventory is matched once. Safe from any thread.
/// </summary>
public sealed class HardFarmsCheck : IHardFarmsCheck
{
    private sealed record Result(IReadOnlyList<HardFarmEntry> Entries, FeedHardFarms? Value);

    private readonly HardFarmsViewModel _hardFarms;
    private readonly ConditionalWeakTable<CharacterInventory, Result> _cache = new();

    public HardFarmsCheck(HardFarmsViewModel hardFarms)
    {
        _hardFarms = hardFarms;
        hardFarms.EntriesChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public FeedHardFarms? Check(CharacterInventory inventory)
    {
        var entries = _hardFarms.Entries;
        if (_cache.TryGetValue(inventory, out var hit) && ReferenceEquals(hit.Entries, entries))
        {
            return hit.Value;
        }

        var summary = HardFarmsOwnership.Summarize(entries, inventory);
        var value = summary is null ? null : new FeedHardFarms(summary.Owned, summary.Total, FeedHardFarms.Cap(summary.Missing));
        _cache.AddOrUpdate(inventory, new Result(entries, value));
        return value;
    }
}
