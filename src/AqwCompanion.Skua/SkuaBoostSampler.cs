namespace AqwCompanion.Skua;

/// <summary>
/// Turns the four raw boost values the plugin reads (seconds left as of the game's last sync, static until the next one) into the
/// status' <see cref="SkuaBoosts"/>: each number is the seconds left as of <c>ReadAt</c> (0 inactive, -1 active with the elapsed time
/// unknown). The elapsed time is known only for a value the sampler saw change (or saw at an observed login); a plugin that started
/// after the login, or that was cut off from SkuaLink while a value changed, reports -1 until the next login. Skua's getter answers 0
/// when a read fails, so a drop to 0 is believed only on a second zero at least <see cref="ZeroConfirm"/> after the first. Pure; thread-safe.
/// </summary>
public sealed class SkuaBoostSampler(Func<DateTimeOffset> now)
{
    /// <summary>A drop to 0 is believed only when a second zero read comes at least this long after the first (a failed read also answers 0).</summary>
    public static readonly TimeSpan ZeroConfirm = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();
    private readonly int[] _value = new int[4];
    private readonly DateTimeOffset?[] _zeroSince = new DateTimeOffset?[4];
    private readonly DateTimeOffset?[] _sync = new DateTimeOffset?[4];
    private bool _seenLoggedOut;
    private bool _loggedIn;
    private bool _initialised;
    private bool _loginObserved;
    private bool _gap;
    private SkuaBoosts? _last;

    /// <summary>The character logged out or another one logged in: nothing is known, and the next login is one the sampler saw.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            ResetCore();
            _seenLoggedOut = true;
        }
    }

    /// <summary>The pipe to SkuaLink was away: a value that changed meanwhile has an unknown sync time.</summary>
    public void NoteGap()
    {
        lock (_lock)
        {
            _gap = true;
        }
    }

    /// <param name="raw">The four raw values (gold, class, reputation, XP), or null when the read failed (the last values stay).</param>
    public SkuaBoosts? Sample(IReadOnlyList<int>? raw, bool loggedIn, IReadOnlyList<SkuaBoostItem> items)
    {
        lock (_lock)
        {
            if (!loggedIn)
            {
                ResetCore();
                _seenLoggedOut = true;
                return null;
            }

            var t = now();
            if (!_loggedIn)
            {
                _loggedIn = true;
                _loginObserved = _seenLoggedOut;
                _initialised = false;
            }

            if (!_initialised && raw is not { Count: 4 })
            {
                return null;   // nothing was read yet: do not make zeros out of a failed read
            }

            var changed = !_initialised || _last is null;
            for (var i = 0; i < 4; i++)
            {
                var read = raw is { Count: 4 } ? Math.Max(0, raw[i]) : _value[i];
                if (_initialised && _value[i] > 0 && read == 0)
                {
                    var first = _zeroSince[i] ??= t;
                    if (t - first < ZeroConfirm)
                    {
                        read = _value[i];
                    }
                }
                else
                {
                    _zeroSince[i] = null;
                }

                if (read != _value[i] || !_initialised)
                {
                    changed |= read != _value[i];
                    _value[i] = read;
                    _sync[i] = read > 0 && !_gap && (_initialised || _loginObserved) ? t : null;
                }
            }

            _gap = false;
            _initialised = true;
            if (!changed && _last is not null && _last.Items is { } known && known.SequenceEqual(items))
            {
                return _last;
            }

            _last = new SkuaBoosts(Wire(0, t), Wire(1, t), Wire(2, t), Wire(3, t), t.ToUnixTimeMilliseconds(), items);
            return _last;
        }
    }

    private int Wire(int i, DateTimeOffset t)
    {
        if (_value[i] <= 0)
        {
            return 0;
        }

        return _sync[i] is not { } at ? SkuaProtocol.BoostTimeUnknown : Math.Max(1, _value[i] - (int)Math.Floor(Math.Max(0, (t - at).TotalSeconds)));
    }

    private void ResetCore()
    {
        Array.Clear(_value);
        Array.Clear(_zeroSince);
        Array.Clear(_sync);
        _loggedIn = false;
        _initialised = false;
        _loginObserved = false;
        _gap = false;
        _last = null;
    }
}
