using AqwCompanion.Core.Reputation;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Characters;

/// <summary>Skua's faction standing in the shape the reputation views read.</summary>
public static class CharacterFactions
{
    public static FactionStanding ToStanding(SkuaFaction f) => new(f.Id, f.Name, f.Rank, f.TotalRep, f.RequiredRep, f.Rep);
}
