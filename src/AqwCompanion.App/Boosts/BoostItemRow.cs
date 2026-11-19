using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.Boosts;

/// <summary>One time line of the boosts card.</summary>
public sealed record BoostRow(string Text);

/// <summary>One boost in the bag: its text ("GOLD Boost! (1 hr) x33"), whether the service allows using it and the command that opens the confirmation.</summary>
public sealed class BoostItemRow(int itemId, string text, bool canUse, IRelayCommand useCommand)
{
    public int ItemId { get; } = itemId;

    public string Text { get; } = text;

    public bool CanUse { get; } = canUse;

    public IRelayCommand UseCommand { get; } = useCommand;
}
