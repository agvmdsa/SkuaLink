using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Characters;

/// <summary>
/// Builds the phone's character list: live data wins, else the last known. Live characters by name, then remembered ones most
/// recently seen first, then connected Skua with no character in hub order.
/// </summary>
public static class PhoneCharacters
{
    public static IReadOnlyList<PhoneCharacter> Build(IReadOnlyList<CharacterRecord> records, SkuaHubView skua, IHardFarmsCheck? hardFarms)
    {
        var rows = new List<(int Group, long Order, string Key, PhoneCharacter Row)>();
        foreach (var r in records)
        {
            var live = skua.Instances.FirstOrDefault(i => Connected(i) && i.Status is { LoggedIn: true } s && Same(s.Character, r.Name));
            var last = live ?? skua.Instances.LastOrDefault(i => Same(i.Label, r.Name));
            var liveFactions = live?.Status?.Factions is { Count: > 0 } f ? f : null;
            var (state, farms) = HardFarms(r, hardFarms);
            rows.Add((live is null ? 1 : 0, live is null ? -r.LastSeenUtc.UtcTicks : 0, r.Name, new PhoneCharacter(
                r.Name, true, last?.InstanceId, live is not null, r.LastSeenUtc,
                live?.Status?.Profile ?? r.Profile, liveFactions ?? r.Factions, liveFactions is not null,
                state, farms, r.InventoryAtUtc, r.Inventory?.Truncated ?? false)));
        }

        var order = 0;
        foreach (var i in skua.Instances.Where(i => Connected(i) && i.Status is not { LoggedIn: true, Character: { Length: > 0 } }))
        {
            rows.Add((2, order++, i.Label, new PhoneCharacter(i.Label, false, i.InstanceId, true, null, null, null, false, PhoneHardFarmsState.NoInventory, null, null, false)));
        }

        return [.. rows.OrderBy(x => x.Group).ThenBy(x => x.Order).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Row)];
    }

    private static (PhoneHardFarmsState, FeedHardFarms?) HardFarms(CharacterRecord r, IHardFarmsCheck? check)
    {
        if (r.Inventory is null)
        {
            return (PhoneHardFarmsState.NoInventory, null);
        }

        return check?.Check(CharacterInventories.For(r.Inventory)) is { } result ? (PhoneHardFarmsState.Ready, result) : (PhoneHardFarmsState.NoList, null);
    }

    private static bool Connected(SkuaInstanceView i) => !i.Disconnected && i.Connection == SkuaConnection.Connected;

    private static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
