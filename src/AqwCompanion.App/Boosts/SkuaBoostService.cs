using AqwCompanion.App.Updates;
using AqwCompanion.Phone.Boosts;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.App.Boosts;

/// <summary>
/// One <see cref="BoostTracker"/> per Skua instance, fed from the Skua feed. Memory only: nothing is stored, and the hub's Forget (it
/// empties the instance list) drops everything on the next pump. Never throws into the hub or the UI.
/// <para>
/// Locks: <c>_gate</c> only. The feed is read before it is taken (with a stamp taken before the read); the announce callback,
/// <see cref="Changed"/>, <c>onError</c> and the command channel are never called while it is held. The hub calls <see cref="Views"/> under its own lock, so the order is hub, then service
/// (this class never calls the hub under <c>_gate</c>). <see cref="Views"/> and <see cref="Find"/> are quick and never throw.
/// </para>
/// </summary>
public sealed class SkuaBoostService : ISkuaBoosts, IDisposable
{
    private readonly ISkuaFeed _skua;
    private readonly ISkuaCommandChannel _channel;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<Notice> _announce;
    private readonly string _appVersion;
    private readonly Action<Exception>? _onError;
    private const int MaxAttempts = 3;

    private readonly object _gate = new();
    private readonly List<Slot> _slots = [];
    private readonly EventHandler _onFeed;
    private long _stamp;     // taken before every read of the feed
    private long _applied;   // under _gate: the newest stamp that was applied
    private readonly CancellationTokenSource _stop = new();
    private int _started;
    private int _disposed;

    public SkuaBoostService(
        ISkuaFeed skua, ISkuaCommandChannel channel, Func<DateTimeOffset> now, Action<Notice> announce, string appVersion, Action<Exception>? onError = null)
    {
        _skua = skua;
        _channel = channel;
        _now = now;
        _announce = announce;
        _appVersion = appVersion;
        _onError = onError;
        _onFeed = (_, _) => Pump();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<BoostView> Views
    {
        get
        {
            try
            {
                var now = _now();   // before the lock
                lock (_gate)
                {
                    return [.. _slots.Select(s => s.View(now))];
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Report(ex);
                return [];
            }
        }
    }

    public BoostView? Find(string instanceId)
    {
        try
        {
            var now = _now();   // before the lock
            lock (_gate)
            {
                return _slots.FirstOrDefault(s => s.InstanceId == instanceId)?.View(now);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
            return null;
        }
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _skua.Changed += _onFeed;
        Pump();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _skua.Changed -= _onFeed;
            _stop.Cancel();
        }
    }

    public void Pump() => Pump(MaxAttempts);

    private void Pump(int attempts)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var stamp = Interlocked.Increment(ref _stamp);   // before the read
        List<Notice> notices = [];
        var changed = false;
        var stale = false;
        try
        {
            var world = _skua.Current;   // read before the lock
            lock (_gate)
            {
                if (stamp <= _applied)
                {
                    stale = true;   // a newer feed was applied while this thread waited; the read may still have been the newer one, so look again below
                }
                else
                {
                    _applied = stamp;
                    var now = _now();
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var instance in world.Instances)
                    {
                        seen.Add(instance.InstanceId);
                        var slot = _slots.Find(s => s.InstanceId == instance.InstanceId);
                        if (slot is null)
                        {
                            slot = new Slot(instance.InstanceId);
                            _slots.Add(slot);
                            changed = true;
                        }

                        foreach (var type in slot.Apply(instance, now, _appVersion))
                        {
                            notices.Add(BoostNotices.Ended(slot.Character ?? instance.Label, type, now));
                        }

                        changed |= slot.TakeSignatureChange(now);
                    }

                    changed |= _slots.RemoveAll(s => !seen.Contains(s.InstanceId)) > 0;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
            return;
        }

        foreach (var notice in notices)
        {
            Safe(() => _announce(notice));
        }

        if (changed || notices.Count > 0)
        {
            RaiseChanged();
        }

        if (stale && attempts > 1)
        {
            Pump(attempts - 1);   // a fresh stamp, outside the lock; bounded
        }
    }

    private void RaiseChanged()
    {
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            Safe(() => handler(this, EventArgs.Empty));   // one throwing listener does not stop the next
        }
    }

    public Task<BoostResult> UseAsync(string instanceId, int itemId, CancellationToken ct = default) =>
        SendAsync(instanceId, BoostGuards.CheckUse(Find(instanceId), itemId), new UseBoostCommand("", itemId), ct);

    public Task<BoostResult> ReloginAsync(string instanceId, CancellationToken ct = default) =>
        SendAsync(instanceId, BoostGuards.CheckRelogin(Find(instanceId)), new ReloginCommand(""), ct);

    private async Task<BoostResult> SendAsync(string instanceId, string? refusal, SkuaCommand command, CancellationToken ct)
    {
        if (refusal is not null)
        {
            return BoostResult.Refused(refusal);
        }

        ResultMessage? result;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
            result = await _channel.SendCommandAsync(instanceId, command, linked.Token).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return BoostResult.Refused(BoostTexts.UnknownSkua);
        }
        catch (InvalidOperationException)
        {
            return BoostResult.Refused(BoostTexts.Disconnected);
        }
        catch (OperationCanceledException)
        {
            return BoostResult.Refused(BoostTexts.NoAnswer);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
            return BoostResult.Refused(BoostTexts.NoAnswer);
        }

        if (result is null)
        {
            return BoostResult.Unanswered;
        }

        Pump();   // the plugin's new status usually follows; look at the feed now as well (does nothing after Dispose)
        return result.Ok ? BoostResult.Done : BoostResult.Refused(string.IsNullOrWhiteSpace(result.Error) ? BoostTexts.Refused : result.Error);
    }

    private void Report(Exception ex) => Safe(() => _onError?.Invoke(ex));

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A listener must never break the service or the caller.
        }
    }

    /// <summary>One Skua instance: its tracker and what the views need. Only touched under the service's lock.</summary>
    private sealed class Slot(string instanceId)
    {
        private readonly BoostTracker _tracker = new();
        private string _signature = "";
        private bool _sawBoosts;
        private bool _oldPlugin;
        private bool _supported;
        private bool _connected;
        private bool _loggedIn;

        public string InstanceId { get; } = instanceId;

        public string? Character { get; private set; }

        public IReadOnlyList<string> Apply(SkuaInstanceView instance, DateTimeOffset now, string appVersion)
        {
            var status = instance.Status;
            _loggedIn = status?.LoggedIn == true;
            _connected = !instance.Disconnected && instance.Connection == SkuaConnection.Connected;
            if (_loggedIn && !string.IsNullOrWhiteSpace(status!.Character))
            {
                Character = status.Character;
            }

            _sawBoosts |= status?.Boosts is not null;
            _oldPlugin |= _loggedIn && status!.Boosts is null && !_sawBoosts;   // a logged-in status without boosts is definitively an old plugin
            _supported = _sawBoosts
                || (!_oldPlugin && !_loggedIn && instance.Hello is { } hello && PluginRule.Evaluate(appVersion, null, [hello.PluginVersion]) is null);
            return _tracker.Observe(new BoostInput(_loggedIn ? status!.Character : null, _loggedIn, _connected, status?.Boosts), now);
        }

        /// <summary>True when something other than the passing of time changed since the last call.</summary>
        public bool TakeSignatureChange(DateTimeOffset now)
        {
            var view = View(now);
            var signature = string.Join('|', view.Character, view.Supported, view.Online, view.CanRelogin, view.Gold != 0, view.Class != 0, view.Rep != 0, view.Xp != 0,
                string.Join(';', view.Items.Select(i => $"{i.ItemId},{i.Name},{i.Type},{i.Quantity},{i.CanUse}")));
            if (signature == _signature)
            {
                return false;
            }

            _signature = signature;
            return true;
        }

        public BoostView View(DateTimeOffset now)
        {
            var r = _tracker.Read(now);
            var items = r.Items
                .Select(i => new BoostItemView(i.ItemId, i.Name, i.Type, i.Quantity, _supported && r.Online && i.Quantity > 0 && i.Type != "unknown" && !r.Active(i.Type)))
                .ToList();
            return new BoostView(
                InstanceId, Character, _supported, r.Online, now.ToUnixTimeMilliseconds(), r.Gold, r.Class, r.Rep, r.Xp, items,
                CanRelogin: _supported && _connected && !_loggedIn);
        }
    }
}
