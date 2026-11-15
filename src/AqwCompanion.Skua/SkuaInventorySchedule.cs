namespace AqwCompanion.Skua;

/// <summary>
/// When the plugin reads the inventory. Each container read is one Flash call whose reply is the JSON of every item, so a big
/// bank costs real time on Skua's UI thread: after a pass the next one waits at least <see cref="CostFactor"/> times what the
/// pass took (never less than <see cref="MinGap"/>, never more than <see cref="Periodic"/>). Item events only ask
/// (<see cref="Request"/>); without a request a pass happens every <see cref="Periodic"/> (manual play raises no events in
/// Skua). Thread-safe: requests come from Skua's event threads, the rest from the plugin's worker.
/// </summary>
public sealed class SkuaInventorySchedule(Func<DateTimeOffset> now)
{
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Periodic = TimeSpan.FromMinutes(5);
    public const int CostFactor = 10;

    private readonly object _lock = new();
    private DateTimeOffset? _lastStart;
    private bool _requested;
    private TimeSpan _gap = MinGap;

    public TimeSpan Gap
    {
        get
        {
            lock (_lock)
            {
                return _gap;
            }
        }
    }

    public void Request()
    {
        lock (_lock)
        {
            _requested = true;
        }
    }

    /// <summary>True when never read (since the last <see cref="Reset"/>), when the periodic pass is due, or when asked and the gap has passed.</summary>
    public bool Due()
    {
        lock (_lock)
        {
            if (_lastStart is not { } last)
            {
                return true;
            }

            var since = now() - last;
            return since < TimeSpan.Zero || since >= Periodic || (_requested && since >= _gap);
        }
    }

    public void Started()
    {
        lock (_lock)
        {
            _lastStart = now();
            _requested = false;
        }
    }

    public void Finished(TimeSpan took)
    {
        lock (_lock)
        {
            var cost = TimeSpan.FromTicks(Math.Min(Math.Max(0, took.Ticks) * CostFactor, Periodic.Ticks));
            _gap = cost > MinGap ? cost : MinGap;
        }
    }

    /// <summary>
    /// A pass that found no bag (still loading after the login, or the read failed): it is not a completed snapshot, so the
    /// next pass is asked for after <see cref="MinGap"/> and its cost does not widen the gap.
    /// </summary>
    public void Retry()
    {
        lock (_lock)
        {
            _requested = true;
            _gap = MinGap;
        }
    }

    /// <summary>A logout, another character or a lost connection: the next pass happens at once.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _lastStart = null;
            _requested = false;
            _gap = MinGap;
        }
    }
}
