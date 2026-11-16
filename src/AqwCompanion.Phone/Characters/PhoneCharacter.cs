using AqwCompanion.Core.Feed;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Characters;

/// <summary>Why a character has Hard Farms numbers or not: no inventory known yet, no list loaded, or ready.</summary>
public enum PhoneHardFarmsState { Ready, NoList, NoInventory }

/// <summary>
/// One row of the phone's character list: a character the PC knows (live or remembered) or a connected Skua with no character
/// logged in (<paramref name="HasCharacter"/> false, <paramref name="Name"/> is the Skua label). <paramref name="InstanceId"/> is
/// the live Skua, else the newest Skua labelled with the character, else null. <paramref name="FactionsLive"/> says whether
/// <paramref name="Factions"/> come from the live Skua or the last known read.
/// </summary>
public sealed record PhoneCharacter(
    string Name,
    bool HasCharacter,
    string? InstanceId,
    bool Live,
    DateTimeOffset? LastSeenUtc,
    SkuaProfile? Profile,
    IReadOnlyList<SkuaFaction>? Factions,
    bool FactionsLive,
    PhoneHardFarmsState HardFarmsState,
    FeedHardFarms? HardFarms,
    DateTimeOffset? InventoryAtUtc,
    bool InventoryTruncated);
