namespace AqwCompanion.Skua;

/// <summary>
/// QuestAccepted and QuestTurnedIn are no longer sent (the status carries the active quests instead); they stay so
/// logs kept by an older version still load.
/// </summary>
public enum SkuaLogKind
{
    Bought, Sold, Dropped, MapChanged, QuestAccepted, QuestTurnedIn, Died, LoggedOut, Relogin, ScriptStarted, ScriptStopped, ScriptError,
}

/// <summary>
/// <paramref name="InstanceId"/> is a random id the plugin makes each time it is loaded, so the app can tell several Skua
/// apart and recognise one that reconnects. Null from an older plugin.
/// </summary>
public sealed record SkuaHello(string PluginVersion, string SkuaVersion, string? InstanceId = null);

/// <summary>An active quest. <paramref name="Ready"/> is Skua's own "can be turned in" flag.</summary>
public sealed record SkuaQuest(int Id, string Name, bool Ready);

/// <summary>
/// A faction standing as Skua reads it (<c>world.myAvatar.factions</c>). <paramref name="Rep"/> is the reputation earned
/// inside the current rank (the game's iSpillRep), <paramref name="RequiredRep"/> the size of the current rank step
/// (iRepToRank, 0 at the maximum rank), <paramref name="TotalRep"/> the faction's total (iRep).
/// </summary>
public sealed record SkuaFaction(int Id, string Name, int Rank, int TotalRep, int Rep, int RequiredRep);

/// <summary>
/// The character's numbers as the plugin reads them from Skua (the status' <c>profile</c>; absent from an older plugin).
/// <paramref name="ClassName"/>, <paramref name="ClassId"/> and <paramref name="ClassPoints"/> come from the equipped class item of
/// the plugin's last inventory read (Skua has no class points property); null parts are not reported.
/// </summary>
public sealed record SkuaProfile(
    int Level, long Xp, long XpToLevel, long Gold, int Coins, int ClassRank, bool IsMember,
    string? ClassName = null, int? ClassId = null, int? ClassPoints = null, string? Guild = null,
    int? BagSlots = null, int? BankSlots = null, int? HouseSlots = null);

/// <summary>One owned item: its name, quantity and container (<see cref="SkuaProtocol.InventoryContainers"/>).</summary>
public sealed record SkuaItem(string Name, int Quantity, string Where);

/// <summary>
/// A full inventory snapshot. <paramref name="Read"/> lists the containers the plugin actually read: the receiver replaces only
/// those and keeps what it knew of the others (the bank is known only once it was opened in Skua). Equal by content.
/// </summary>
public sealed record SkuaInventory(string? Character, IReadOnlyList<string> Read, IReadOnlyList<SkuaItem> Items, bool Truncated = false)
{
    public bool Equals(SkuaInventory? other) =>
        other is not null && Character == other.Character && SkuaLists.Same(Read, other.Read) && SkuaLists.Same(Items, other.Items) && Truncated == other.Truncated;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Character);
        SkuaLists.Add(ref hash, Read);
        SkuaLists.Add(ref hash, Items);
        hash.Add(Truncated);
        return hash.ToHashCode();
    }
}

/// <summary>A boost item of the bag: <paramref name="Type"/> is one of <see cref="SkuaProtocol.BoostTypes"/>.</summary>
public sealed record SkuaBoostItem(int ItemId, string Name, string Type, int Quantity);

/// <summary>
/// The boosts as the plugin reads them (the status' <c>boosts</c>; absent from an older plugin). Each of the four numbers is the seconds
/// left as of <paramref name="ReadAt"/> (unix ms): 0 inactive, <see cref="SkuaProtocol.BoostTimeUnknown"/> active with the elapsed
/// time unknown, otherwise the seconds. <paramref name="Items"/> are the boost items of the bag (at most
/// <see cref="SkuaProtocol.MaxBoostItems"/>); null when not reported. Equal by content.
/// </summary>
public sealed record SkuaBoosts(int Gold, int Class, int Rep, int Xp, long ReadAt, IReadOnlyList<SkuaBoostItem>? Items = null)
{
    public bool Equals(SkuaBoosts? other) =>
        other is not null && Gold == other.Gold && Class == other.Class && Rep == other.Rep && Xp == other.Xp && ReadAt == other.ReadAt
        && SkuaLists.Same(Items, other.Items);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Gold);
        hash.Add(Class);
        hash.Add(Rep);
        hash.Add(Xp);
        hash.Add(ReadAt);
        SkuaLists.Add(ref hash, Items);
        return hash.ToHashCode();
    }
}

/// <summary>
/// What Skua is doing. <paramref name="Quests"/> null means "this plugin does not report quests" (an older plugin);
/// an empty list means "no active quests". <paramref name="Factions"/> follows the same rule for the reputation. Two
/// statuses are equal when their quest lists and their faction lists hold the same entries in the same order.
/// <paramref name="Control"/> is one of <see cref="SkuaProtocol.ControlStates"/>, or null when not reported (an older
/// plugin); <paramref name="SettleSecondsLeft"/> is the settle countdown, only while settling; <paramref name="OptionsStuck"/>
/// says the plugin could not close a script's options window on the PC. <paramref name="Profile"/> is the character's
/// numbers, null when not reported. <paramref name="Boosts"/> is null when not reported.
/// </summary>
public sealed record SkuaStatus(
    bool LoggedIn, string? Character, string? Server, string? Map, string? Script, bool ScriptRunning, DateTimeOffset? RunningSince,
    IReadOnlyList<SkuaQuest>? Quests = null, IReadOnlyList<SkuaFaction>? Factions = null,
    string? Control = null, int? SettleSecondsLeft = null, bool OptionsStuck = false, SkuaProfile? Profile = null, SkuaBoosts? Boosts = null)
{
    public bool Equals(SkuaStatus? other) =>
        other is not null
        && LoggedIn == other.LoggedIn
        && Character == other.Character
        && Server == other.Server
        && Map == other.Map
        && Script == other.Script
        && ScriptRunning == other.ScriptRunning
        && RunningSince == other.RunningSince
        && SkuaLists.Same(Quests, other.Quests)
        && SkuaLists.Same(Factions, other.Factions)
        && Control == other.Control
        && SettleSecondsLeft == other.SettleSecondsLeft
        && OptionsStuck == other.OptionsStuck
        && Profile == other.Profile
        && Boosts == other.Boosts;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(LoggedIn);
        hash.Add(Character);
        hash.Add(Server);
        hash.Add(Map);
        hash.Add(Script);
        hash.Add(ScriptRunning);
        hash.Add(RunningSince);
        SkuaLists.Add(ref hash, Quests);
        SkuaLists.Add(ref hash, Factions);
        hash.Add(Control);
        hash.Add(SettleSecondsLeft);
        hash.Add(OptionsStuck);
        hash.Add(Profile);
        hash.Add(Boosts);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A log line. A drop whose item name is known carries it in <paramref name="Item"/>, and <paramref name="Count"/> says how
/// many drops of it SkuaLink has merged into this line (1 to <see cref="SkuaProtocol.MaxCount"/>). Every other line, and
/// lines kept by an older version, have no item and a count of 1.
/// </summary>
public sealed record SkuaLogEntry(DateTimeOffset At, SkuaLogKind Kind, string Text, string? Item = null, int Count = 1);

public abstract record SkuaMessage;

public sealed record HelloMessage(SkuaHello Hello) : SkuaMessage;

public sealed record StatusMessage(SkuaStatus Status) : SkuaMessage;

public sealed record LogMessage(SkuaLogEntry Entry) : SkuaMessage;

public sealed record InventoryMessage(SkuaInventory Inventory) : SkuaMessage;

/// <summary>A well-formed line of another protocol version; the receiver tells the user to update.</summary>
public sealed record UnsupportedVersionMessage(int Version) : SkuaMessage;
