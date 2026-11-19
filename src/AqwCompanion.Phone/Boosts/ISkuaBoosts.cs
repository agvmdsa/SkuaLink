namespace AqwCompanion.Phone.Boosts;

public sealed record BoostItemView(int ItemId, string Name, string Type, int Quantity, bool CanUse);

/// <summary>One Skua's boosts as the PC card and the phone show them. The four numbers are the seconds left as of ReadAt (the PC clock, unix ms): 0 inactive, -1 active with the time unknown. Online: logged in and connected (the clock runs).</summary>
public sealed record BoostView(
    string InstanceId, string? Character, bool Supported, bool Online, long ReadAt, int Gold, int Class, int Rep, int Xp,
    IReadOnlyList<BoostItemView> Items, bool CanRelogin)
{
    public int ValueOf(string type) => type switch { "gold" => Gold, "class" => Class, "rep" => Rep, "xp" => Xp, _ => 0 };

    public bool Active(string type) => ValueOf(type) != 0;
}

public sealed record BoostResult(bool Ok, string? Error = null, bool NoAnswer = false)
{
    public static BoostResult Done { get; } = new(true);

    public static BoostResult Refused(string text) => new(false, text);

    public static BoostResult Unanswered { get; } = new(false, BoostTexts.NoAnswer, true);
}

/// <summary>
/// What the phone API, the phone hub and the PC card see of the boosts. <see cref="Views"/> and <see cref="Find"/> are quick and
/// never throw; <see cref="Changed"/> is raised outside every lock of the service.
/// </summary>
public interface ISkuaBoosts
{
    IReadOnlyList<BoostView> Views { get; }

    BoostView? Find(string instanceId);

    Task<BoostResult> UseAsync(string instanceId, int itemId, CancellationToken ct = default);

    Task<BoostResult> ReloginAsync(string instanceId, CancellationToken ct = default);

    event EventHandler? Changed;
}
