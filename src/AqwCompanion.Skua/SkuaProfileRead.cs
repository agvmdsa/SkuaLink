namespace AqwCompanion.Skua;

/// <summary>What the plugin read from <c>IScriptPlayer</c> for one status (one Flash call per field, see the plugin README).</summary>
public readonly record struct SkuaProfileReading(int Level, int Xp, int RequiredXp, int Gold, int Coins, bool IsMember, string? Guild, int ClassRank);

/// <summary>The equipped class item of the last inventory pass (Skua has no class points property; the item's quantity is used).</summary>
public readonly record struct SkuaClassFacts(int Id, string Name, int Quantity);

/// <summary>The slot totals (<c>ILimitedInventory.Slots</c>) of bag, bank and house, read with the inventory pass.</summary>
public readonly record struct SkuaSlotFacts(int Bag, int Bank, int House);

/// <summary>The pure rule the plugin follows to build the status' profile, kept here so it is tested without Skua.</summary>
public static class SkuaProfileRead
{
    /// <summary>
    /// The profile to report. A failed read keeps the last one (none before the first good read): null, or a level of 0 or less,
    /// because Skua's getters catch a failed Flash call and return the default (0, false, null) instead of throwing, and a
    /// loaded character is at least level 1. Negative numbers become 0; a blank guild, a class id or
    /// a slot total of 0 or less are not reported; texts are trimmed and cut to <see cref="SkuaProtocol.MaxTextLength"/>.
    /// </summary>
    public static SkuaProfile? Next(SkuaProfile? last, SkuaProfileReading? read, SkuaClassFacts? equippedClass, SkuaSlotFacts? slots)
    {
        if (read is not { Level: > 0 } r)
        {
            return last;
        }

        return new SkuaProfile(
            Math.Max(0, r.Level), Math.Max(0, r.Xp), Math.Max(0, r.RequiredXp), Math.Max(0, r.Gold), Math.Max(0, r.Coins), Math.Max(0, r.ClassRank), r.IsMember,
            Text(equippedClass?.Name), equippedClass is { Id: > 0 } c ? c.Id : null, equippedClass is { } q ? Math.Max(0, q.Quantity) : null,
            Text(r.Guild), Positive(slots?.Bag), Positive(slots?.Bank), Positive(slots?.House));
    }

    /// <summary>
    /// The profile to put in the status: none while the character is still loading (no inventory pass has found a bag yet and
    /// the level reads 0), so a status never carries a profile of zeros.
    /// </summary>
    public static SkuaProfile? Report(SkuaProfile? profile, bool bagSeen) =>
        profile is not null && (bagSeen || profile.Level > 0) ? profile : null;

    private static int? Positive(int? value) => value is > 0 ? value : null;

    private static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : SkuaText.Cut(value.Trim(), SkuaProtocol.MaxTextLength);
}
