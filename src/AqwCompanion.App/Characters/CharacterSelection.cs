using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Skua;

namespace AqwCompanion.App.Characters;

/// <summary>
/// The character the desktop (Hard Farms, the local feed) shows: the one chosen by hand while the book knows it, else the
/// character of the first connected, logged-in Skua (hub order) the book knows, else the most recently seen one, else none.
/// Names come in the book's casing. Safe from any thread; <see cref="Changed"/> is raised (on the thread of the change)
/// only when <see cref="Selected"/> differs from the value last reported.
/// </summary>
public sealed class CharacterSelection
{
    private readonly CharacterBook _book;
    private readonly ISkuaFeed _skua;
    private readonly object _lock = new();
    private string? _chosen;
    private string? _reported;

    public CharacterSelection(CharacterBook book, ISkuaFeed skua)
    {
        _book = book;
        _skua = skua;
        _reported = Resolve(null);
        book.Changed += (_, _) => Update();
        skua.Changed += (_, _) => Update();
    }

    public event EventHandler? Changed;

    /// <summary>Every known character name, sorted by name.</summary>
    public IReadOnlyList<string> Names => [.. _book.All.Select(r => r.Name)];

    /// <summary>The selected character's name, or null while the book knows no character.</summary>
    public string? Selected
    {
        get
        {
            string? chosen;
            lock (_lock)
            {
                chosen = _chosen;
            }

            return Resolve(chosen);
        }
    }

    /// <summary>Chooses a character by name (ignoring case); null (or a name the book does not know) goes back to the automatic choice.</summary>
    public void Choose(string? name)
    {
        lock (_lock)
        {
            _chosen = string.IsNullOrWhiteSpace(name) ? null : name;
        }

        Update();
    }

    private string? Resolve(string? chosen)
    {
        if (_book.Find(chosen) is { } picked)
        {
            return picked.Name;
        }

        foreach (var instance in _skua.Current.Instances)
        {
            if (!instance.Disconnected && instance.Connection == SkuaConnection.Connected
                && instance.Status is { LoggedIn: true, Character: { Length: > 0 } character }
                && _book.Find(character) is { } live)
            {
                return live.Name;
            }
        }

        return _book.All.MaxBy(r => r.LastSeenUtc)?.Name;
    }

    private void Update()
    {
        // Resolved outside the lock: the book and the hub take their own locks, and readers always resolve afresh.
        var now = Selected;
        lock (_lock)
        {
            if (string.Equals(now, _reported, StringComparison.Ordinal))
            {
                return;
            }

            _reported = now;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
