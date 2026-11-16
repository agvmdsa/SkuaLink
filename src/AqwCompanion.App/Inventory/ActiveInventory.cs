using AqwCompanion.App.Characters;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Phone.Characters;

namespace AqwCompanion.App.Inventory;

/// <summary>
/// The inventory the rest of the app reads: the last known inventory Skua reported for the selected character, or null while
/// there is none. Raises <see cref="Changed"/> through <c>onUiThread</c> (inline without it), only when <see cref="Current"/>
/// becomes a different instance.
/// </summary>
public sealed class ActiveInventory : IActiveInventory
{
    private readonly CharacterBook _book;
    private readonly CharacterSelection _selection;
    private readonly Action<Action> _onUiThread;
    private readonly object _lock = new();
    private CharacterInventory? _reported;

    public ActiveInventory(CharacterBook book, CharacterSelection selection, Action<Action>? onUiThread = null)
    {
        _book = book;
        _selection = selection;
        _onUiThread = onUiThread ?? (action => action());
        _reported = Current;
        book.Changed += (_, _) => Update();
        selection.Changed += (_, _) => Update();
    }

    /// <summary>Raised on the UI thread when the selected character's inventory changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The last known inventory of the selected character (bag, house and bank together), or null.</summary>
    public CharacterInventory? Current =>
        _book.Find(_selection.Selected)?.Inventory is { } inventory ? CharacterInventories.For(inventory) : null;

    private void Update()
    {
        var now = Current;
        lock (_lock)
        {
            if (ReferenceEquals(now, _reported))
            {
                return;
            }

            _reported = now;
        }

        _onUiThread(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
