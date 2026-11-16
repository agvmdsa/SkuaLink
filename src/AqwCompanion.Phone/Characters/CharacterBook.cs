using AqwCompanion.Core.Forgetting;
using AqwCompanion.Skua;

namespace AqwCompanion.Phone.Characters;

/// <summary>
/// The last known profile, reputation and inventory of every character Skua reported (at most <see cref="MaxCharacters"/>,
/// the longest unseen goes first), kept across runs through an <see cref="ICharacterBookStore"/>. UI-free; safe from any
/// thread. State changes happen under one lock; <see cref="Changed"/> is raised and the store written outside it, and a
/// throwing handler never stops the others.
/// </summary>
/// <remarks>
/// Saves are throttled to one per <see cref="SaveEvery"/>; <see cref="Flush"/> (also on <see cref="Dispose"/>) writes what is
/// left. Snapshots are taken and written under one I/O lock, so writes land in order, and a Forget deletes only after a write in
/// progress. A save before <see cref="Restore"/> restores first,
/// so the stored characters are never overwritten by the live ones only.
/// </remarks>
public sealed class CharacterBook(ICharacterBookStore store, Func<DateTimeOffset> now) : IForgettable, IDisposable
{
    /// <summary>At most this many characters are kept.</summary>
    public const int MaxCharacters = 20;

    /// <summary>
    /// At most this many items are kept across all characters, so the stored book stays one small database document; beyond
    /// it the longest unseen characters lose their inventory contents first (their profile and reputation stay).
    /// </summary>
    public const int MaxStoredItems = 60_000;

    /// <summary>The store is written at most once per this interval, except by <see cref="Flush"/>.</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(30);

    /// <summary>The order the containers of a merged inventory follow when they first appear.</summary>
    private static readonly IReadOnlyList<string> ContainerOrder =
        [SkuaProtocol.ContainerInventory, SkuaProtocol.ContainerHouse, SkuaProtocol.ContainerBank];

    private readonly object _lock = new();
    private readonly object _io = new();
    private readonly Dictionary<string, CharacterRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _lastSave;
    private bool _dirty;
    private bool _quiesced;
    private bool _restored;

    public event EventHandler? Changed;

    public string Name => "Characters";

    /// <summary>Every known character, sorted by name (ordinal, ignoring case).</summary>
    public IReadOnlyList<CharacterRecord> All
    {
        get
        {
            lock (_lock)
            {
                return [.. _records.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    /// <summary>The character of that name (ignoring case), or null.</summary>
    public CharacterRecord? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (_lock)
        {
            return _records.GetValueOrDefault(name);
        }
    }

    /// <summary>Adds the stored characters not known yet (live ones win). Runs once.</summary>
    public void Restore()
    {
        lock (_lock)
        {
            if (_restored)
            {
                return;
            }

            _restored = true;
        }

        var stored = store.Load();
        var added = false;
        bool live;
        lock (_lock)
        {
            live = _records.Count > 0;
            foreach (var record in stored)
            {
                if (record is null || string.IsNullOrWhiteSpace(record.Name) || _records.ContainsKey(record.Name)
                    || _records.Count >= MaxCharacters)
                {
                    continue;
                }

                _records[record.Name] = record;
                added = true;
            }

            if (added)
            {
                Prune(null);

                // Live characters may have been saved alone while the stored book was being read: write them together.
                _dirty |= live;
            }
        }

        if (added && live)
        {
            Save(force: true);
        }

        if (added)
        {
            Raise();
        }
    }

    /// <summary>A status of a Skua: a logged-in one records its character, profile and factions.</summary>
    public void OnStatus(SkuaStatus status)
    {
        if (!status.LoggedIn || string.IsNullOrWhiteSpace(status.Character))
        {
            return;
        }

        var name = status.Character;
        bool visible;
        lock (_lock)
        {
            if (_quiesced)
            {
                return;
            }

            var at = now();
            if (!_records.TryGetValue(name, out var record))
            {
                record = new CharacterRecord(name, at);
                visible = true;
                Add(record);
            }
            else
            {
                visible = !string.Equals(record.Name, name, StringComparison.Ordinal);
                record = record with { Name = name, LastSeenUtc = at };
            }

            if (status.Profile is { } profile && profile != record.Profile)
            {
                record = record.Profile is null
                    ? record with { Profile = profile, ProfileAtUtc = at }
                    : record with { Profile = profile, ProfileAtUtc = at, ProfileChangedAtUtc = at, ProfileChanges = record.ProfileChanges + 1 };
                visible = true;
            }

            if (status.Factions is { Count: > 0 } factions && (record.Factions is null || !factions.SequenceEqual(record.Factions)))
            {
                record = record with { Factions = [.. factions], FactionsAtUtc = at };
                visible = true;
            }

            _records[name] = record;
            _dirty |= visible;
        }

        if (visible)
        {
            Save(force: false);
            Raise();
        }
    }

    /// <summary>
    /// An inventory snapshot of that character: the containers it read replace what was known of them, the others stay.
    /// </summary>
    public void OnInventory(string? character, SkuaInventory inventory)
    {
        if (string.IsNullOrWhiteSpace(character))
        {
            return;
        }

        lock (_lock)
        {
            if (_quiesced)
            {
                return;
            }

            var at = now();
            var record = _records.GetValueOrDefault(character);
            var merged = Merge(record?.Name ?? character, record?.Inventory, inventory);
            if (record is not null && merged.Equals(record.Inventory))
            {
                return;
            }

            if (record is null)
            {
                record = new CharacterRecord(character, at, Inventory: merged, InventoryAtUtc: at);
                Add(record);
            }
            else
            {
                record = record with { Inventory = merged, InventoryAtUtc = at };
            }

            _records[record.Name] = record;
            Prune(record.Name);
            _dirty = true;
        }

        Save(force: false);
        Raise();
    }

    /// <summary>Stamps when that character was last seen (its Skua closed).</summary>
    public void Touch(string? character)
    {
        if (string.IsNullOrWhiteSpace(character))
        {
            return;
        }

        lock (_lock)
        {
            if (_quiesced || !_records.TryGetValue(character, out var record))
            {
                return;
            }

            _records[character] = record with { LastSeenUtc = now() };
            _dirty = true;
        }

        Save(force: false);
        Raise();
    }

    /// <summary>Writes the unsaved changes now.</summary>
    public void Flush() => Save(force: true);

    /// <summary>Stops recording and saving; returns once a write in progress has finished, so the database can be deleted.</summary>
    public void Quiesce()
    {
        lock (_lock)
        {
            _quiesced = true;
        }

        lock (_io)
        {
            // Only waits for a write in progress.
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _quiesced = false;
        }
    }

    /// <summary>Drops every character and deletes the stored book.</summary>
    public void Forget()
    {
        lock (_io)
        {
            lock (_lock)
            {
                _records.Clear();
                _restored = true;
                _dirty = false;
                _lastSave = null;
            }

            store.Delete();
        }

        lock (_lock)
        {
            _quiesced = false;
        }

        Raise();
    }

    public void Dispose() => Flush();

    /// <summary>Under the lock: adds a new record, dropping the longest unseen one beyond <see cref="MaxCharacters"/>.</summary>
    private void Add(CharacterRecord record)
    {
        _records[record.Name] = record;
        if (_records.Count > MaxCharacters)
        {
            var oldest = _records.Values.Where(r => r != record).MinBy(r => r.LastSeenUtc);
            if (oldest is not null)
            {
                _records.Remove(oldest.Name);
            }
        }
    }

    private static SkuaInventory Merge(string character, SkuaInventory? stored, SkuaInventory incoming)
    {
        var read = new List<string>();
        foreach (var container in (stored?.Read ?? []).Concat(incoming.Read))
        {
            if (ContainerOrder.Contains(container) && !read.Contains(container))
            {
                read.Add(container);
            }
        }

        var items = new List<SkuaItem>();
        foreach (var container in read)
        {
            var source = incoming.Read.Contains(container) ? incoming.Items : stored?.Items ?? [];
            items.AddRange(source.Where(i => i.Where == container));
        }

        return new SkuaInventory(character, read, items, incoming.Truncated);
    }

    /// <summary>
    /// Writes the unsaved changes. The snapshot is taken and written under the I/O lock, so writes land in the order of their
    /// snapshots and an older one is never written after a newer one. A throttled save never waits for a write in progress:
    /// it leaves the changes for the next save or <see cref="Flush"/>.
    /// </summary>
    private void Save(bool force)
    {
        bool restoreFirst;
        lock (_lock)
        {
            restoreFirst = !_restored && _dirty && !_quiesced;
        }

        if (restoreFirst)
        {
            // A save before the restore would overwrite the stored characters with the live ones only.
            Restore();
        }

        var entered = false;
        try
        {
            if (force)
            {
                Monitor.Enter(_io, ref entered);
            }
            else
            {
                Monitor.TryEnter(_io, ref entered);
            }

            if (!entered)
            {
                return;
            }

            List<CharacterRecord> snapshot;
            lock (_lock)
            {
                var at = now();
                if (!_dirty || _quiesced || (!force && _lastSave is { } last && at - last < SaveEvery))
                {
                    return;
                }

                snapshot = [.. _records.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
                _dirty = false;
                _lastSave = at;
            }

            if (!store.Save(snapshot))
            {
                lock (_lock)
                {
                    _dirty = true;
                }
            }
        }
        finally
        {
            if (entered)
            {
                Monitor.Exit(_io);
            }
        }
    }

    /// <summary>
    /// Under the lock: beyond <see cref="MaxStoredItems"/>, the longest unseen characters lose their inventory contents first;
    /// <paramref name="fed"/> (the character whose inventory just arrived) never does.
    /// </summary>
    private void Prune(string? fed)
    {
        var total = _records.Values.Sum(r => r.Inventory?.Items.Count ?? 0);
        while (total > MaxStoredItems)
        {
            var oldest = _records.Values
                .Where(r => r.Inventory is not null && (fed is null || !string.Equals(r.Name, fed, StringComparison.OrdinalIgnoreCase)))
                .MinBy(r => r.LastSeenUtc);
            if (oldest is null)
            {
                return;
            }

            total -= oldest.Inventory!.Items.Count;
            _records[oldest.Name] = oldest with { Inventory = null, InventoryAtUtc = null };
        }
    }

    private void Raise()
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // One listener must never stop the others.
            }
        }
    }
}
