using AqwCompanion.App.Infrastructure;
using AqwCompanion.Phone.Notices;

namespace AqwCompanion.App.Updates;

public enum UpdateStatus { NeverChecked, Off, Checking, UpToDate, Available, CouldNotCheck }

/// <param name="Available">The release on offer (strictly newer than this app); kept across a failed check.</param>
/// <param name="Latest">The version of the latest ready release known, newer or not.</param>
public sealed record UpdateState(UpdateStatus Status, ReleaseInfo? Available, ReleaseVersion? Latest, DateTimeOffset? LastCheckUtc)
{
    public static UpdateState Initial { get; } = new(UpdateStatus.NeverChecked, null, null, null);

    public bool IsAvailable => Available is not null;
}

public interface IUpdateState
{
    UpdateState Current { get; }

    /// <summary>Raised after every change of <see cref="Current"/>, from any thread.</summary>
    event EventHandler? Changed;
}

/// <summary>The notice the phones get for a new release.</summary>
public static class UpdateNotice
{
    public static Notice For(ReleaseVersion version, DateTimeOffset at) =>
        new($"update:{version}", NoticeKind.UpdateAvailable, string.Empty, "SkuaLink update available", $"SkuaLink {version} is available. Update it on the PC.", at);
}

/// <summary>
/// Asks GitHub for the latest release 30 seconds after <see cref="Start"/> and every 6 hours, or when told to. Nothing is asked while
/// the switch is off. A newer release is announced once per version through <c>announce</c>. Never throws.
/// </summary>
public sealed class UpdateChecker : IUpdateState, IDisposable
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IGitHubReleases _releases;
    private readonly string _appVersion;
    private readonly Func<bool> _enabled;
    private readonly IDelay _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<string> _announced;
    private readonly Action<string> _remember;
    private readonly Action<Notice>? _announce;
    private readonly Action<Exception>? _onError;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private UpdateState _state = UpdateState.Initial;
    private ReleaseInfo? _latest;
    private string? _etag;
    private int _generation;
    private int _busy;
    private int _started;

    public UpdateChecker(
        IGitHubReleases releases, string appVersion, Func<bool> enabled, IDelay delay, Func<DateTimeOffset> now, Func<string> announced,
        Action<string> remember, Action<Notice>? announce = null, Action<Exception>? onError = null)
    {
        _releases = releases;
        _appVersion = appVersion;
        _enabled = enabled;
        _delay = delay;
        _now = now;
        _announced = announced;
        _remember = remember;
        _announce = announce;
        _onError = onError;
    }

    public event EventHandler? Changed;

    public UpdateState Current
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>Starts the timer loop; safe to call again.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _ = Task.Run(LoopAsync);
        }
    }

    public async Task CheckNowAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            // A result dropped because the switch went off and on during the request gets one more pass, never more.
            for (var pass = 0; pass < 2; pass++)
            {
                if (!await PassAsync(ct).ConfigureAwait(false) || !SafeEnabled())
                {
                    break;
                }
            }
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private bool SafeEnabled()
    {
        try
        {
            return _enabled();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
            return false;
        }
    }

    /// <summary>One request. Returns true when its result was dropped as stale.</summary>
    private async Task<bool> PassAsync(CancellationToken ct)
    {
        var generation = 0;
        try
        {
            if (!_enabled())
            {
                SetOff();
                return false;
            }

            UpdateState before;
            string? etag;
            lock (_lock)
            {
                generation = _generation;
                before = _state;
                etag = _etag;
                _state = before with { Status = UpdateStatus.Checking };
            }

            RaiseChanged();
            ReleaseCheck result;
            try
            {
                result = await _releases.GetLatestAsync(etag, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                SetIfCurrent(generation, before);
                return false;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Report(ex);
                result = new ReleaseCheck(ReleaseCheckStatus.Failed);
            }

            return Apply(result, generation);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                Report(ex);
            }

            FailIfChecking(generation);
            return false;
        }
    }

    /// <summary>The switch changed: off clears the state without a request, on checks at once.</summary>
    public Task ReconfigureAsync()
    {
        if (_enabled())
        {
            return CheckNowAsync();
        }

        SetOff();
        return Task.CompletedTask;
    }

    public void Dispose() => _stop.Cancel();

    private async Task LoopAsync()
    {
        try
        {
            await _delay.DelayAsync(FirstCheckDelay, _stop.Token).ConfigureAwait(false);
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await CheckNowAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    Report(ex); // A tick must never end the loop.
                }

                await _delay.DelayAsync(Interval, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
        }
    }

    private bool Apply(ReleaseCheck result, int generation)
    {
        if (!_enabled())
        {
            SetOff();
            return false;
        }

        var now = _now();
        ReleaseInfo? newer = null;
        lock (_lock)
        {
            if (generation != _generation)
            {
                return true; // The switch went off while the request ran.
            }

            if (result.Status == ReleaseCheckStatus.Failed)
            {
                _state = _state with { Status = UpdateStatus.CouldNotCheck, LastCheckUtc = now };
            }
            else
            {
                if (result.Status == ReleaseCheckStatus.Found)
                {
                    _latest = result.Release;
                    _etag = result.ETag;
                }
                else if (result.Status == ReleaseCheckStatus.NotReady)
                {
                    _latest = null;
                    _etag = result.ETag;
                }

                // NotModified keeps both. Never a downgrade, never the same version; a pre-release app compares by its numeric version.
                newer = _latest is { } latest && ReleaseVersion.TryParseApp(_appVersion, out var app, out _) && latest.Version > app ? _latest : null;
                _state = new UpdateState(newer is null ? UpdateStatus.UpToDate : UpdateStatus.Available, newer, _latest?.Version, now);
            }
        }

        RaiseChanged();
        if (newer is not null)
        {
            Announce(newer, now);
        }

        return false;
    }

    private void Announce(ReleaseInfo release, DateTimeOffset now)
    {
        try
        {
            var version = release.Version.ToString();
            if (_announce is null || _announced() == version)
            {
                return;
            }

            _announce(UpdateNotice.For(release.Version, now));
            _remember(version);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(ex);
        }
    }

    private void SetOff()
    {
        lock (_lock)
        {
            _generation++;
            _latest = null;
            _etag = null;
            _state = new UpdateState(UpdateStatus.Off, null, null, _state.LastCheckUtc);
        }

        RaiseChanged();
    }

    private void SetIfCurrent(int generation, UpdateState state)
    {
        lock (_lock)
        {
            if (generation != _generation)
            {
                return;
            }

            _state = state;
        }

        RaiseChanged();
    }

    private void FailIfChecking(int generation)
    {
        lock (_lock)
        {
            if (generation != _generation || _state.Status != UpdateStatus.Checking)
            {
                return;
            }

            _state = _state with { Status = UpdateStatus.CouldNotCheck };
        }

        RaiseChanged();
    }

    // Each subscriber on its own, outside the lock: a throwing one must not break the checker or the others.
    private void RaiseChanged()
    {
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler)handler)(this, EventArgs.Empty);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Report(ex);
            }
        }
    }

    private void Report(Exception ex)
    {
        try
        {
            _onError?.Invoke(ex);
        }
        catch (Exception reporter) when (reporter is not OutOfMemoryException)
        {
            // The reporter must not throw into the caller.
        }
    }
}
