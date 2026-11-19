namespace AqwCompanion.Phone.Boosts;

/// <summary>The checks that run on the PC before any boost command is sent. Null means allowed; otherwise the refusal text.</summary>
public static class BoostGuards
{
    public static string? CheckUse(BoostView? view, int itemId)
    {
        if (view is null)
        {
            return BoostTexts.Disconnected;
        }

        if (!view.Supported)
        {
            return BoostTexts.NotSupported;
        }

        if (!view.Online)
        {
            return BoostTexts.NotLoggedIn;
        }

        var item = view.Items.FirstOrDefault(i => i.ItemId == itemId);
        if (item is null || item.Quantity <= 0)
        {
            return BoostTexts.NotInBag;
        }

        if (!BoostTracker.Types.Contains(item.Type))
        {
            return BoostTexts.UnknownType;
        }

        return view.Active(item.Type) ? BoostTexts.AlreadyActive(item.Type) : null;
    }

    public static string? CheckRelogin(BoostView? view)
    {
        if (view is null)
        {
            return BoostTexts.Disconnected;
        }

        if (!view.Supported)
        {
            return BoostTexts.NotSupported;
        }

        if (view.Online)
        {
            return BoostTexts.AlreadyLoggedIn;
        }

        return view.CanRelogin ? null : BoostTexts.Disconnected;
    }
}
