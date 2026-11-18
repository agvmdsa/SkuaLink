using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Boosts;

public sealed record BoostInput(string? Character, bool LoggedIn, bool Connected, SkuaBoosts? Boosts);

/// <summary>Seconds left per type as of the time asked: 0 inactive, -1 active with the time unknown; never 0 while a type is active.</summary>
public sealed record BoostReading(bool Known, bool Online, int Gold, int Class, int Rep, int Xp, IReadOnlyList<SkuaBoostItem> Items)
{
    public int ValueOf(string type) => type switch { "gold" => Gold, "class" => Class, "rep" => Rep, "xp" => Xp, _ => 0 };

    public bool Active(string type) => ValueOf(type) != 0;
}

/// <summary>
/// The arithmetic and the end detection of one Skua's boosts, with the clock passed in. Online time (a counter) advances only while
/// the plugin is connected AND the character is logged in. A NEW reading anchors each type at <c>onlineNow - age</c>, where age is
/// the time since its <c>readAt</c> (so an app started later still counts the time since the reading); a reading equal to the last
/// one (the plugin resends its last status on every reconnect) changes nothing. The first reading after a login, a reconnect, a
/// logout or a character change only sets the baseline, so it ends nothing. Not thread-safe.
/// </summary>
public sealed class BoostTracker
{
    public static readonly IReadOnlyList<string> Types = ["gold", "class", "rep", "xp"];

    private readonly int[] _value = new int[4];
    private readonly double[] _anchor = new double[4];
    private IReadOnlyList<SkuaBoostItem> _items = [];
    private string? _character;
    private SkuaBoosts? _lastReading;
    private bool _hasReading;
    private bool _break = true;
    private bool _online;
    private double _onlineSeconds;
    private DateTimeOffset _lastTick;

    /// <returns>The types that ended with this observation (a type that was active and is now 0), in <see cref="Types"/> order.</returns>
    public IReadOnlyList<string> Observe(BoostInput input, DateTimeOffset now)
    {
        Advance(now);
        if (input.LoggedIn && !string.IsNullOrWhiteSpace(input.Character))
        {
            if (_character is not null && !string.Equals(_character, input.Character, StringComparison.OrdinalIgnoreCase))
            {
                ClearValues();
                _break = true;
            }

            _character = input.Character;
        }

        var online = input.LoggedIn && input.Connected;
        if (!online)
        {
            _online = false;
            _break = true;
            return [];
        }

        if (!_online)
        {
            _break = true;
        }

        _online = true;
        if (input.Boosts is not { } boosts || (_hasReading && boosts == _lastReading))
        {
            return [];
        }

        var incoming = new[] { boosts.Gold, boosts.Class, boosts.Rep, boosts.Xp };
        var readAt = DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(boosts.ReadAt, 0, 253402300799999));
        var age = Math.Max(0, (now - readAt).TotalSeconds);
        var ended = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var value = Math.Max(-1, incoming[i]);
            if (!_break && _hasReading && _value[i] != 0 && value == 0)
            {
                ended.Add(Types[i]);
            }

            _value[i] = value;
            _anchor[i] = _onlineSeconds - age;
        }

        _items = boosts.Items ?? [];
        _lastReading = boosts;
        _hasReading = true;
        _break = false;
        return ended;
    }

    public BoostReading Read(DateTimeOffset now)
    {
        var onlineNow = _onlineSeconds + (_online ? Math.Max(0, (now - _lastTick).TotalSeconds) : 0);
        int Left(int i) => _value[i] <= 0 ? _value[i] : Math.Max(1, (int)Math.Ceiling(_value[i] - (onlineNow - _anchor[i])));
        return new BoostReading(_hasReading, _online, Left(0), Left(1), Left(2), Left(3), _items);
    }

    public void Reset()
    {
        ClearValues();
        _character = null;
        _online = false;
        _break = true;
    }

    private void Advance(DateTimeOffset now)
    {
        if (_online && now > _lastTick)
        {
            _onlineSeconds += (now - _lastTick).TotalSeconds;
        }

        _lastTick = now;
    }

    private void ClearValues()
    {
        Array.Clear(_value);
        Array.Clear(_anchor);
        _items = [];
        _lastReading = null;
        _hasReading = false;
    }
}
