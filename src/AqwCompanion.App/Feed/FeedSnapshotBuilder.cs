using System.Globalization;
using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Feed;
using AqwCompanion.Core.Reputation;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Skua;

namespace AqwCompanion.App.Feed;

/// <summary>
/// Turns the characters Skua reported, the Skua connections, the selected character and its Hard Farms progress into the
/// snapshot the local feed serves (contract 1: <c>capture</c> describes Skua, <c>daysPlayed</c> is 0 and <c>lastKill</c> null).
/// </summary>
public static class FeedSnapshotBuilder
{
    public static FeedSnapshot Build(IReadOnlyList<CharacterRecord> records, SkuaHubView skua, string? selected, HardFarmsSummary? hardFarms)
    {
        var live = LiveCharacters(skua);
        var characters = records
            .Where(r => r.Profile is not null)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var p = r.Profile!;
                var isLive = live.Contains(r.Name);
                return new FeedCharacter(
                    r.Name, p.Level, isLive, r.ProfileAtUtc ?? r.LastSeenUtc, isLive ? r.ProfileChangedAtUtc : null, r.ProfileChanges,
                    new FeedFigures(p.Gold, p.Xp, p.XpToLevel, p.Coins, p.ClassPoints ?? 0, 0), null);
            })
            .ToList();

        var chosen = selected is null ? null : records.FirstOrDefault(r => string.Equals(r.Name, selected, StringComparison.OrdinalIgnoreCase));
        var factions = chosen?.Factions?
            .Select(CharacterFactions.ToStanding)
            .OrderBy(FactionProgress.Missing)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FeedFaction(
                f.Name, f.Rank, f.Rep,
                FactionProgress.IsMax(f) ? 0 : Math.Max(0, (long)f.RepToRank - f.SpillRep),
                FactionProgress.Fraction(f), FactionProgress.IsMax(f)))
            .ToList() ?? [];

        var connected = ConnectedCount(skua);
        return new FeedSnapshot(
            FeedSnapshot.ContractNumber,
            new FeedCapture(SkuaStateText(connected), connected > 0),
            chosen?.Name,
            characters,
            factions,
            chosen is null || hardFarms is null ? null : new FeedHardFarms(hardFarms.Owned, hardFarms.Total, FeedHardFarms.Cap(hardFarms.Missing)));
    }

    /// <summary>The Skua connection in words, for the feed's <c>capture.state</c>.</summary>
    public static string SkuaStateText(int connected) => connected switch
    {
        <= 0 => "Skua not connected.",
        1 => "1 Skua connected.",
        _ => string.Create(CultureInfo.InvariantCulture, $"{connected} Skua connected."),
    };

    /// <summary>How many Skua with the SkuaLink plugin are connected now.</summary>
    public static int ConnectedCount(SkuaHubView skua) =>
        skua.Instances.Count(i => !i.Disconnected && i.Connection == SkuaConnection.Connected);

    /// <summary>The characters logged in in a connected Skua (ignoring case).</summary>
    internal static HashSet<string> LiveCharacters(SkuaHubView skua) =>
        skua.Instances
            .Where(i => !i.Disconnected && i.Connection == SkuaConnection.Connected && i.Status is { LoggedIn: true, Character: { Length: > 0 } })
            .Select(i => i.Status!.Character!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
