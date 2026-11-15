using AqwCompanion.Core.Inventory;

namespace AqwCompanion.App.Inventory;

/// <summary>The inventory the rest of the app reads, so a feature can be tested without Skua.</summary>
public interface IActiveInventory
{
    /// <summary>Raised on the UI thread when the inventory may have changed.</summary>
    event EventHandler? Changed;

    /// <summary>The selected character's last known inventory as Skua reported it, or null while there is none.</summary>
    CharacterInventory? Current { get; }
}
