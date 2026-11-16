using AqwCompanion.Core.Feed;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Notices;

/// <summary>Turns the stream of snapshots, Skua views (log entries and statuses) and the passing of time into notices. Pure: no clock, no I/O; the caller passes <c>now</c>. Not thread-safe: the caller (the hub) serialises every call.</summary>
public sealed class NoticeEngine(NoticeOptions? options = null, PhoneStops? stops = null)
{
    private const int RememberedIds = 512;
    private const int MaxErrorLine = 120;
    private const string StartedPrefix = "Script started: ";
    private const string StoppedPrefix = "Script stopped: ";
    private const string ErrorPrefix = "Script stopped with an error: ";
    private const string KickedPrefix = "Disconnected by the server";

    private readonly NoticeOptions _options = options ?? NoticeOptions.Default;
    private readonly HashSet<string> _seen = [];
    private readonly Queue<string> _seenOrder = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _recent = new(StringComparer.Ordinal);
    private FeedSnapshot? _previous;
    private Dictionary<string, bool>? _skuaConnected;
    private readonly HashSet<string> _optionsWaits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LogCursor> _cursors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _script = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _wasRunning = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastErrorAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastKick = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dying> _dying = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Progress> _progress = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SkuaInventory?> _inventories = new(StringComparer.OrdinalIgnoreCase);
    private bool _haveView;

    /// <summary>Forgets everything: the next snapshot and the next Skua view are baselines again and no notice id is remembered.</summary>
    public void Reset()
    {
        _previous = null;
        _skuaConnected = null;
        _optionsWaits.Clear();
        _seen.Clear();
        _seenOrder.Clear();
        _recent.Clear();
        _cursors.Clear();
        _script.Clear();
        _wasRunning.Clear();
        _lastErrorAt.Clear();
        _lastKick.Clear();
        _dying.Clear();
        _progress.Clear();
        _inventories.Clear();
        _haveView = false;
    }

    public IReadOnlyList<Notice> Process(FeedSnapshot snapshot, DateTimeOffset now)
    {
        var found = new List<Notice>();
        if (_previous is { } before)
        {
            Logins(before, snapshot, now, found);
            Ranks(before, snapshot, now, found);
            HardFarmDrops(before, snapshot, now, found);
        }

        _previous = snapshot;
        return Admit(found, now);
    }

    /// <summary>
    /// The Skua instances changed. Connection: a connected Skua that is now disconnected raises <see cref="NoticeKind.SkuaDisconnected"/>, a disconnected
    /// one that is connected again <see cref="NoticeKind.SkuaReconnected"/>; the first view is a baseline. A connected Skua whose status says
    /// <c>optionsStuck</c> raises <see cref="NoticeKind.SkuaOptionsWaiting"/> once per wait (a wait already there in the baseline is not announced).
    /// Log: new entries of an instance raise the script, kick and dying notices (the first sight of an instance is a baseline). Progress: see
    /// <see cref="NoProgress"/>.
    /// </summary>
    public IReadOnlyList<Notice> ProcessSkua(SkuaHubView view, DateTimeOffset now)
    {
        var found = new List<Notice>();
        OptionsWaiting(view, now, baseline: _skuaConnected is null, found);
        var connected = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var instance in view.Instances)
        {
            connected[instance.InstanceId] = !instance.Disconnected;
            if (_skuaConnected is null || !_skuaConnected.TryGetValue(instance.InstanceId, out var was) || was == !instance.Disconnected)
            {
                continue;
            }

            var label = instance.Label;
            found.Add(instance.Disconnected
                ? new Notice($"skua-off:{instance.InstanceId}:{now.UtcTicks}", NoticeKind.SkuaDisconnected, label, "Skua disconnected", $"{label} is no longer connected to SkuaLink.", now)
                : new Notice($"skua-on:{instance.InstanceId}:{now.UtcTicks}", NoticeKind.SkuaReconnected, label, "Skua reconnected", $"{label} is connected to SkuaLink again.", now));
        }

        _skuaConnected = connected;
        LogEntries(view, now, found);
        UpdateProgress(view, now);
        _haveView = true;
        Rearm(now);
        NoProgress(now, found);
        return Admit(found, now);
    }

    /// <summary>Time passing with no new view: a stalled character is found here, and a dying character is armed again.</summary>
    public IReadOnlyList<Notice> Tick(DateTimeOffset now)
    {
        var found = new List<Notice>();
        if (_haveView)
        {
            Rearm(now);
            NoProgress(now, found);
        }

        return Admit(found, now);
    }

    /// <summary>The character book changed: a different inventory snapshot of a watched character is progress.</summary>
    public void NoteProgress(IReadOnlyList<CharacterRecord> records, DateTimeOffset now)
    {
        foreach (var record in records)
        {
            var known = _inventories.TryGetValue(record.Name, out var before);
            _inventories[record.Name] = record.Inventory;
            if (known && !Equals(before, record.Inventory) && _progress.TryGetValue(record.Name, out var state))
            {
                state.Restart(now);
            }
        }
    }

    // ---- Skua log entries ----

    private void LogEntries(SkuaHubView view, DateTimeOffset now, List<Notice> found)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instance in view.Instances)
        {
            var id = instance.InstanceId;
            present.Add(id);
            if (!_cursors.TryGetValue(id, out var cursor))
            {
                _cursors[id] = LogCursor.Baseline(instance.Log, now);
                _wasRunning[id] = instance.Status?.ScriptRunning ?? false;
                continue;
            }

            var fresh = instance.Log.Where(cursor.IsNew).OrderBy(e => e.At).ToList();
            var running = _wasRunning.GetValueOrDefault(id) || instance.Status?.ScriptRunning == true;
            foreach (var entry in fresh)
            {
                cursor.Advance(entry);
                Entry(instance, entry, fresh, running, now, found);
            }

            _wasRunning[id] = instance.Status?.ScriptRunning ?? false;
            if (instance.Status is { ScriptRunning: true, Script: { Length: > 0 } script })
            {
                _script[id] = script;
            }
        }

        foreach (var gone in _cursors.Keys.Where(k => !present.Contains(k)).ToList())
        {
            _cursors.Remove(gone);
            _script.Remove(gone);
            _wasRunning.Remove(gone);
            _lastErrorAt.Remove(gone);
        }
    }

    private void Entry(SkuaInstanceView instance, SkuaLogEntry entry, List<SkuaLogEntry> batch, bool running, DateTimeOffset now, List<Notice> found)
    {
        var id = instance.InstanceId;
        var label = Who(instance);
        switch (entry.Kind)
        {
            case SkuaLogKind.ScriptStarted:
                if (After(entry.Text, StartedPrefix) is { Length: > 0 } started)
                {
                    _script[id] = started;
                }

                break;
            case SkuaLogKind.ScriptStopped:
                if (!SuppressedByPhone(id, now) && !PairedWithError(id, entry, batch))
                {
                    var script = After(entry.Text, StoppedPrefix) is { Length: > 0 } named ? named : _script.GetValueOrDefault(id);
                    found.Add(new Notice(
                        $"skua-stopped:{id}:{entry.At.UtcTicks}", NoticeKind.ScriptStopped, label, "Script stopped",
                        script is null ? $"{label}: the script stopped." : $"{label}: {script} stopped.", now));
                }

                break;
            case SkuaLogKind.ScriptError:
                _lastErrorAt[id] = entry.At;
                {
                    // A failure always notifies: the phone-stop suppression is for ScriptStopped only.
                    var script = _script.GetValueOrDefault(id) ?? instance.Status?.Script;
                    var line = Cut(FirstLine(After(entry.Text, ErrorPrefix) ?? entry.Text), MaxErrorLine);
                    var name = script is null ? "the script" : script;
                    found.Add(new Notice(
                        $"skua-error:{id}:{entry.At.UtcTicks}", NoticeKind.ScriptError, label, "Script failed",
                        line.Length == 0 ? $"{label}: {name} failed." : $"{label}: {name} failed: {line}", now));
                }

                break;
            case SkuaLogKind.LoggedOut:
            case SkuaLogKind.Relogin when entry.Text.StartsWith(KickedPrefix, StringComparison.Ordinal):
                if (running && (!_lastKick.TryGetValue(label, out var last) || now - last >= _options.KickCooldown))
                {
                    _lastKick[label] = now;
                    found.Add(new Notice(
                        $"skua-kicked:{id}:{entry.At.UtcTicks}", NoticeKind.CharacterKicked, label, "Character disconnected",
                        $"{label} was disconnected while a script was running.", now));
                }

                break;
            case SkuaLogKind.Died:
                Death(instance, entry, now, found);
                break;
        }
    }

    /// <summary>The character the notice is about: the status character name, the instance label only when there is none. Keys every per-character state.</summary>
    private static string Who(SkuaInstanceView instance) => instance.Status?.Character is { Length: > 0 } name ? name : instance.Label;

    private bool SuppressedByPhone(string instanceId, DateTimeOffset now) => stops?.Within(instanceId, now, _options.PhoneStopWindow) == true;

    private bool PairedWithError(string instanceId, SkuaLogEntry stop, List<SkuaLogEntry> batch)
    {
        var pair = _options.ErrorStopPair;
        if (_lastErrorAt.TryGetValue(instanceId, out var at) && (stop.At - at).Duration() <= pair)
        {
            return true;
        }

        return batch.Any(e => e.Kind == SkuaLogKind.ScriptError && (stop.At - e.At).Duration() <= pair);
    }

    // ---- dying ----

    private void Death(SkuaInstanceView instance, SkuaLogEntry entry, DateTimeOffset now, List<Notice> found)
    {
        var label = Who(instance);
        if (!_dying.TryGetValue(label, out var state))
        {
            _dying[label] = state = new Dying();
        }

        state.Times.Add(entry.At);
        state.Prune(now, _options.DyingWindow);
        if (state.Times.Count < _options.DyingCount || !state.Armed)
        {
            return;
        }

        state.Armed = false;
        state.BelowSince = null;
        var script = instance.Status?.Script ?? _script.GetValueOrDefault(instance.InstanceId);
        var map = instance.Status?.Map;
        var where = (script, map) switch
        {
            ({ Length: > 0 } s, { Length: > 0 } m) => $" (script {s}, map {m})",
            ({ Length: > 0 } s, _) => $" (script {s})",
            (_, { Length: > 0 } m) => $" (map {m})",
            _ => string.Empty,
        };
        var minutes = Math.Max(1, (int)_options.DyingWindow.TotalMinutes);
        found.Add(new Notice(
            $"dying:{label}:{entry.At.UtcTicks}", NoticeKind.DyingOften, label, $"{label} keeps dying",
            $"{label} died {_options.DyingCount} times in {minutes} minutes{where}.", now));
    }

    /// <summary>
    /// Re-arm timing: the clean time starts at the first evaluation (a view or a Tick) that sees fewer than DyingCount deaths in the
    /// window, not at the moment the window actually emptied; the caller's Tick keeps this within a tick of the exact time.
    /// </summary>
    private void Rearm(DateTimeOffset now)
    {
        foreach (var state in _dying.Values)
        {
            state.Prune(now, _options.DyingWindow);
            if (state.Times.Count >= _options.DyingCount)
            {
                state.BelowSince = null;
                continue;
            }

            state.BelowSince ??= now;
            if (!state.Armed && now - state.BelowSince >= _options.DyingRearm)
            {
                state.Armed = true;
            }
        }
    }

    // ---- no progress ----

    private void UpdateProgress(SkuaHubView view, DateTimeOffset now)
    {
        var watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in view.Instances)
        {
            if (instance.Disconnected || instance.Status is not { LoggedIn: true, Character: { Length: > 0 } name } status)
            {
                continue;
            }

            watched.Add(name);
            var fingerprint = Fingerprint.Of(status);
            if (!_progress.TryGetValue(name, out var state))
            {
                _progress[name] = new Progress(fingerprint, now);
            }
            else if (state.Fingerprint != fingerprint)
            {
                state.Fingerprint = fingerprint;
                state.Restart(now);
            }
        }

        foreach (var gone in _progress.Keys.Where(k => !watched.Contains(k)).ToList())
        {
            _progress.Remove(gone);
        }
    }

    private void NoProgress(DateTimeOffset now, List<Notice> found)
    {
        var minutes = Math.Max(1, (int)_options.NoProgressAfter.TotalMinutes);
        foreach (var (name, state) in _progress)
        {
            if (state.Fired || now - state.Since < _options.NoProgressAfter)
            {
                continue;
            }

            state.Fired = true;
            found.Add(new Notice(
                $"noprogress:{name}:{state.Since.UtcTicks}", NoticeKind.NoProgress, name, "No progress", $"{name} has not made progress for {minutes} minutes.", now));
        }
    }

    // ---- connection, options, feed (unchanged rules) ----

    private void OptionsWaiting(SkuaHubView view, DateTimeOffset now, bool baseline, List<Notice> found)
    {
        var waiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instance in view.Instances)
        {
            if (instance.Disconnected || instance.Status is not { OptionsStuck: true })
            {
                continue;
            }

            waiting.Add(instance.InstanceId);
            if (!_optionsWaits.Add(instance.InstanceId) || baseline)
            {
                continue;
            }

            var label = instance.Label;
            found.Add(new Notice(
                $"skua-options:{instance.InstanceId}:{now.UtcTicks}", NoticeKind.SkuaOptionsWaiting, label,
                "Waiting for you on the PC", $"{label}: close the script's options window on the computer.", now));
        }

        _optionsWaits.IntersectWith(waiting);
    }

    private static void Logins(FeedSnapshot before, FeedSnapshot now, DateTimeOffset at, List<Notice> found)
    {
        var known = before.Characters.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var c in now.Characters.Where(c => !known.Contains(c.Name)))
        {
            found.Add(new Notice($"login:{c.Name}:{at.UtcTicks}", NoticeKind.NewLogin, c.Name, "Character logged in", $"{c.Name} logged in.", at));
        }
    }

    private static void Ranks(FeedSnapshot before, FeedSnapshot now, DateTimeOffset at, List<Notice> found)
    {
        if (now.Selected is null || now.Selected != before.Selected)
        {
            return;
        }

        var old = before.Factions.ToDictionary(f => f.Name, f => f.Rank, StringComparer.Ordinal);
        foreach (var f in now.Factions.Where(f => old.TryGetValue(f.Name, out var rank) && f.Rank > rank))
        {
            found.Add(new Notice($"rank:{now.Selected}:{f.Name}:{f.Rank}", NoticeKind.RankUp, now.Selected, "Rank up", $"{f.Name} reached rank {f.Rank}.", at));
        }
    }

    private static void HardFarmDrops(FeedSnapshot before, FeedSnapshot now, DateTimeOffset at, List<Notice> found)
    {
        if (now.Selected is null || now.Selected != before.Selected
            || before.HardFarms is not { } old || now.HardFarms is not { } current
            || old.Total != current.Total || current.Owned <= old.Owned)
        {
            return;
        }

        var still = current.Missing.ToHashSet(StringComparer.Ordinal);
        foreach (var name in old.Missing.Where(n => !still.Contains(n)))
        {
            found.Add(new Notice($"hf:{now.Selected}:{name}", NoticeKind.HardFarmDrop, now.Selected, "Hard Farm obtained", $"{name} is now owned.", at));
        }
    }

    private List<Notice> Admit(List<Notice> found, DateTimeOffset now)
    {
        var admitted = new List<Notice>();
        foreach (var notice in found)
        {
            if (!_seen.Add(notice.Id))
            {
                continue;
            }

            _seenOrder.Enqueue(notice.Id);
            while (_seenOrder.Count > RememberedIds)
            {
                _seen.Remove(_seenOrder.Dequeue());
            }

            if (!_recent.TryGetValue(notice.Character, out var times))
            {
                _recent[notice.Character] = times = new Queue<DateTimeOffset>();
            }

            while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromMinutes(1))
            {
                times.Dequeue();
            }

            // The id was remembered above on purpose: a notice dropped by the rate limit is not reported later either.
            if (times.Count >= _options.MaxPerCharacterPerMinute)
            {
                continue;
            }

            times.Enqueue(now);
            admitted.Add(notice);
        }

        return admitted;
    }

    // ---- helpers ----

    private static string? After(string text, string prefix) => text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..].Trim() : null;

    private static string FirstLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        return (end < 0 ? text : text[..end]).Trim();
    }

    private static string Cut(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var length = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..length];
    }

    private sealed class LogCursor(DateTimeOffset at, HashSet<string> keys)
    {
        private DateTimeOffset _at = at;
        private readonly HashSet<string> _keys = keys;

        public static LogCursor Baseline(IReadOnlyList<SkuaLogEntry> log, DateTimeOffset now)
        {
            var newest = log.Count > 0 ? log.Max(e => e.At) : DateTimeOffset.MinValue;
            var at = newest > now ? newest : now;
            return new LogCursor(at, [.. log.Where(e => e.At == at).Select(Key)]);
        }

        public bool IsNew(SkuaLogEntry entry) => entry.At > _at || (entry.At == _at && !_keys.Contains(Key(entry)));

        public void Advance(SkuaLogEntry entry)
        {
            if (entry.At > _at)
            {
                _at = entry.At;
                _keys.Clear();
            }

            _keys.Add(Key(entry));
        }

        private static string Key(SkuaLogEntry e) => $"{e.Kind}|{e.Text}";
    }

    private sealed class Dying
    {
        public List<DateTimeOffset> Times { get; } = [];

        public bool Armed { get; set; } = true;

        public DateTimeOffset? BelowSince { get; set; }

        public void Prune(DateTimeOffset now, TimeSpan window) => Times.RemoveAll(t => now - t > window);
    }

    private sealed record Fingerprint(SkuaProfile? Profile, string Factions, string? Map, string? Script, bool Running)
    {
        public static Fingerprint Of(SkuaStatus s) => new(
            s.Profile, s.Factions is null ? string.Empty : string.Join(';', s.Factions.Select(f => $"{f.Id}:{f.TotalRep}:{f.Rank}")), s.Map, s.Script, s.ScriptRunning);
    }

    private sealed class Progress(Fingerprint fingerprint, DateTimeOffset since)
    {
        public Fingerprint Fingerprint { get; set; } = fingerprint;

        public DateTimeOffset Since { get; private set; } = since;

        public bool Fired { get; set; }

        public void Restart(DateTimeOffset now)
        {
            Since = now;
            Fired = false;
        }
    }
}
