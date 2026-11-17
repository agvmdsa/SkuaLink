namespace AqwCompanion.Phone.Notices;

/// <summary>When SkuaLink last sent a start or stop to each Skua at the phone's request, so the notice for the script that ends because of it is not raised. Safe from any thread.</summary>
public sealed class PhoneStops(Func<DateTimeOffset> now)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _at = new(StringComparer.Ordinal);

    public void Note(string instanceId)
    {
        lock (_lock)
        {
            _at[instanceId] = now();
        }
    }

    public bool Within(string instanceId, DateTimeOffset at, TimeSpan window)
    {
        lock (_lock)
        {
            return _at.TryGetValue(instanceId, out var noted) && at - noted <= window;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _at.Clear();
        }
    }
}
