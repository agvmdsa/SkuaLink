using AqwCompanion.Core.Forgetting;

namespace AqwCompanion.Phone.Push;

/// <summary>
/// The push subscriptions of the paired phones, one per device, kept in <c>push.json</c>. Entries that are not valid are dropped on load.
/// Writes only happen when something changed, so a Forget never recreates the file. Errors are kept in memory only.
/// </summary>
public sealed class PushSubscriptionStore : IForgettable
{
    private const int MaxErrorLength = 200;

    private readonly object _lock = new();
    private readonly IPushFileStore _file;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
    private List<PushSubscription> _subs;
    private bool _quiesced;

    public PushSubscriptionStore(IPushFileStore file, Func<DateTimeOffset> now)
    {
        _file = file;
        _now = now;
        _subs = Clean(file.Load());
    }

    public string Name => "Push notifications";

    public IReadOnlyList<PushSubscription> All
    {
        get
        {
            lock (_lock)
            {
                return [.. _subs];
            }
        }
    }

    public PushSubscription? Get(string deviceId)
    {
        lock (_lock)
        {
            return _subs.FirstOrDefault(s => s.DeviceId == deviceId);
        }
    }

    /// <summary>Stores the device's subscription, replacing the one it had. Null kinds keep the device's existing kinds, else all.</summary>
    public PushSubscription Set(string deviceId, string endpoint, string p256dh, string auth, IReadOnlyList<string>? kinds)
    {
        lock (_lock)
        {
            var existing = _subs.FirstOrDefault(s => s.DeviceId == deviceId);
            var chosen = (kinds is null ? null : PushKinds.Normalize(kinds)) ?? existing?.Kinds ?? PushKinds.All;
            var sub = new PushSubscription(deviceId, endpoint, p256dh, auth, chosen, _now(), null);
            if (_quiesced)
            {
                return sub;
            }

            _subs.RemoveAll(s => s.DeviceId == deviceId);
            _subs.Add(sub);
            _errors.Remove(deviceId);
            Save();
            return sub;
        }
    }

    public bool SetKinds(string deviceId, IReadOnlyList<string> kinds)
    {
        lock (_lock)
        {
            var index = _subs.FindIndex(s => s.DeviceId == deviceId);
            if (index < 0 || _quiesced || PushKinds.Normalize(kinds) is not { } normalized)
            {
                return false;
            }

            _subs[index] = _subs[index] with { Kinds = normalized };
            Save();
            return true;
        }
    }

    public bool Remove(string deviceId)
    {
        lock (_lock)
        {
            return RemoveLocked([deviceId]);
        }
    }

    public void RemoveDevices(IReadOnlyList<string> deviceIds)
    {
        lock (_lock)
        {
            RemoveLocked(deviceIds);
        }
    }

    /// <summary>Drops the subscriptions of devices the registry no longer knows (revoked while the app was closed).</summary>
    public void RetainDevices(IReadOnlyCollection<string> knownDeviceIds)
    {
        lock (_lock)
        {
            var known = new HashSet<string>(knownDeviceIds, StringComparer.Ordinal);
            RemoveLocked(_subs.Where(s => !known.Contains(s.DeviceId)).Select(s => s.DeviceId).ToList());
        }
    }

    public void RecordSuccess(string deviceId)
    {
        lock (_lock)
        {
            var index = _subs.FindIndex(s => s.DeviceId == deviceId);
            if (index < 0 || _quiesced)
            {
                return;
            }

            _subs[index] = _subs[index] with { LastSuccessUtc = _now() };
            _errors.Remove(deviceId);
            Save();
        }
    }

    /// <summary>Keeps the text of the last failure in memory only (never in the file).</summary>
    public void RecordError(string deviceId, string error)
    {
        lock (_lock)
        {
            if (_quiesced || _subs.All(s => s.DeviceId != deviceId))
            {
                return;
            }

            _errors[deviceId] = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        }
    }

    public string? LastError(string deviceId)
    {
        lock (_lock)
        {
            return _errors.GetValueOrDefault(deviceId);
        }
    }

    public void Quiesce()
    {
        lock (_lock)
        {
            _quiesced = true;
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _quiesced = false;
        }
    }

    public void Forget()
    {
        lock (_lock)
        {
            _subs = [];
            _errors.Clear();
            _file.Delete();
            _quiesced = false;
        }
    }

    private bool RemoveLocked(IReadOnlyCollection<string> ids)
    {
        if (_quiesced)
        {
            return false;
        }

        var removed = _subs.RemoveAll(s => ids.Contains(s.DeviceId));
        foreach (var id in ids)
        {
            _errors.Remove(id);
        }

        if (removed == 0)
        {
            return false;
        }

        Save();
        return true;
    }

    private void Save() => _file.Save(new PushFile([.. _subs]));

    private static List<PushSubscription> Clean(PushFile? loaded)
    {
        var result = new List<PushSubscription>();
        foreach (var sub in loaded?.Subscriptions ?? [])
        {
            if (sub is null || string.IsNullOrEmpty(sub.DeviceId) || string.IsNullOrEmpty(sub.P256dh) || string.IsNullOrEmpty(sub.Auth)
                || !PushEndpointPolicy.IsAllowed(sub.Endpoint))
            {
                continue;
            }

            var known = (sub.Kinds ?? PushKinds.All).Where(PushKinds.IsKnown).ToList();
            // An empty list the user chose stays empty; a list that held only unknown names becomes all.
            var kinds = known.Count == 0 && sub.Kinds is not { Count: 0 } ? PushKinds.All : PushKinds.Normalize(known)!;
            result.RemoveAll(s => s.DeviceId == sub.DeviceId);
            result.Add(sub with { Kinds = kinds });
        }

        return result;
    }
}
