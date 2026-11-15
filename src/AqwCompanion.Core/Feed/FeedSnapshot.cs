using System.Text.Json;
using System.Text.Json.Serialization;

namespace AqwCompanion.Core.Feed;

/// <summary>
/// What the local feed tells other programs on this PC (contract 1): the characters the Skua plugin reported numbers for (with
/// their last known figures) and, for the selected one, its factions and Hard Farms progress. Only what the dashboard already
/// shows: no ids, e-mail, IP or other players. Fields are only ever added within a contract number; a breaking change raises it.
/// </summary>
public sealed record FeedSnapshot(
    int Contract,
    FeedCapture Capture,
    string? Selected,
    IReadOnlyList<FeedCharacter> Characters,
    IReadOnlyList<FeedFaction> Factions,
    FeedHardFarms? HardFarms)
{
    public const int ContractNumber = 1;

    /// <summary>No character known yet and no Skua connected.</summary>
    public static FeedSnapshot Empty { get; } = new(ContractNumber, new FeedCapture("Skua not connected.", false), null, [], [], null);
}

/// <summary>The Skua connection in words and whether at least one Skua is connected (the field keeps its contract 1 name).</summary>
public sealed record FeedCapture(string State, bool Listening);

/// <summary>
/// One character the Skua plugin reported numbers for, identified by its name. <c>Live</c> is true while it is logged in in a
/// connected Skua; <c>LastLiveUpdateUtc</c> is when its numbers last changed while live and <c>LiveUpdates</c> counts those
/// changes since SkuaLink started; <c>SavedAtUtc</c> is when the numbers were last read. <c>LastKill</c> is always null since 0.6.0.
/// </summary>
public sealed record FeedCharacter(
    string Name,
    int Level,
    bool Live,
    DateTimeOffset SavedAtUtc,
    DateTimeOffset? LastLiveUpdateUtc,
    int LiveUpdates,
    FeedFigures Figures,
    FeedKill? LastKill);

/// <summary>The character's figures from the Skua plugin; <c>DaysPlayed</c> is always 0 since 0.6.0 (Skua does not provide it).</summary>
public sealed record FeedFigures(long Gold, long Exp, long ExpToLevel, int Ac, int ClassPoints, int DaysPlayed);

/// <summary>The gold and experience of a last kill; kept for contract 1 readers, never sent since 0.6.0 (Skua does not provide it).</summary>
public sealed record FeedKill(long Gold, long Exp);

public sealed record FeedFaction(string Name, int Rank, int Rep, long ToNext, double Fraction, bool Max);

/// <summary>How many Hard Farms entries the selected character owns, and the display names of those it does not (at most <see cref="MaxMissing"/>).</summary>
public sealed record FeedHardFarms(int Owned, int Total, IReadOnlyList<string> Missing)
{
    public const int MaxMissing = 200;

    public static IReadOnlyList<string> Cap(IEnumerable<string> names) => [.. names.Take(MaxMissing)];
}

public static class FeedJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(FeedSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);
}
