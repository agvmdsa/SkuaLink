namespace AqwCompanion.Skua;

/// <summary>
/// The pure rules of the plugin's inventory pass, kept here so they are tested without Skua. The bag and the house arrive with
/// the login and are always read together; the bank is filled only once it was opened in Skua this session (the plugin never
/// opens it: that sends a game packet), so an empty bank read counts as "not read" and the app keeps the bank it knew.
/// </summary>
public static class SkuaInventoryRead
{
    /// <summary>
    /// The snapshot to send, or null when there is nothing to report (no bag read, or an empty bag: not logged in or not loaded;
    /// every character owns at least a class). Order: bag, house, bank, so a line cut by the size cap loses the bank first.
    /// The bank counts as read only under <see cref="BankRead"/>: with items and not equal to <paramref name="staleBank"/>.
    /// </summary>
    public static SkuaInventory? Build(
        string? character,
        IReadOnlyList<(string? Name, int Quantity)>? inventory,
        IReadOnlyList<(string? Name, int Quantity)>? bank,
        IReadOnlyList<(string? Name, int Quantity)>? house,
        IReadOnlyList<(string? Name, int Quantity)>? staleBank = null)
    {
        var bag = Clean(inventory);
        if (bag.Count == 0)
        {
            return null;
        }

        var read = new List<string> { SkuaProtocol.ContainerInventory, SkuaProtocol.ContainerHouse };
        var items = new List<SkuaItem>();
        items.AddRange(bag.Select(i => new SkuaItem(i.Name, i.Quantity, SkuaProtocol.ContainerInventory)));
        items.AddRange(Clean(house).Select(i => new SkuaItem(i.Name, i.Quantity, SkuaProtocol.ContainerHouse)));
        var stored = BankRead(bank, staleBank) ? Clean(bank) : [];
        if (stored.Count > 0)
        {
            read.Add(SkuaProtocol.ContainerBank);
            items.AddRange(stored.Select(i => new SkuaItem(i.Name, i.Quantity, SkuaProtocol.ContainerBank)));
        }

        var truncated = Over(inventory) || Over(house) || (stored.Count > 0 && Over(bank));
        return new SkuaInventory(string.IsNullOrWhiteSpace(character) ? null : character.Trim(), read, items, truncated);
    }

    /// <summary>
    /// Whether a bank read counts: it has items (Skua fills <c>world.bankinfo.items</c> only once the bank was opened) and it is
    /// not the stale list (<see cref="StaleBank"/>), compared by cleaned names and quantities in order.
    /// </summary>
    public static bool BankRead(IReadOnlyList<(string? Name, int Quantity)>? bank, IReadOnlyList<(string? Name, int Quantity)>? staleBank)
    {
        var stored = Clean(bank);
        return stored.Count > 0 && (staleBank is null || !stored.SequenceEqual(Clean(staleBank)));
    }

    /// <summary>
    /// The bank list that must not be taken for <paramref name="character"/>'s: the last bank read when it belonged to another
    /// character (whether Skua clears <c>world.bankinfo.items</c> on a relogin to another character is unknown), else null.
    /// </summary>
    public static IReadOnlyList<(string? Name, int Quantity)>? StaleBank(
        string? bankOwner, string? character, IReadOnlyList<(string? Name, int Quantity)>? lastBankRead) =>
        string.Equals(bankOwner?.Trim(), character?.Trim(), StringComparison.OrdinalIgnoreCase) ? null : lastBankRead;

    /// <summary>The equipped item whose category (the game's <c>sType</c>) is Class, ignoring case; null when there is none.</summary>
    public static SkuaClassFacts? EquippedClass(IEnumerable<(int Id, string? Name, int Quantity, bool Equipped, string? Category)> items)
    {
        foreach (var (id, name, quantity, equipped, category) in items)
        {
            if (equipped && !string.IsNullOrWhiteSpace(name) && string.Equals(category?.Trim(), "Class", StringComparison.OrdinalIgnoreCase))
            {
                return new SkuaClassFacts(id, name.Trim(), Math.Max(0, quantity));
            }
        }

        return null;
    }

    private static List<(string Name, int Quantity)> Clean(IReadOnlyList<(string? Name, int Quantity)>? list) =>
        list is null
            ? []
            : [.. list.Where(i => !string.IsNullOrWhiteSpace(i.Name)).Take(SkuaProtocol.MaxInventoryItems).Select(i => (i.Name!.Trim(), Math.Max(0, i.Quantity)))];

    private static bool Over(IReadOnlyList<(string? Name, int Quantity)>? list) =>
        list is not null && list.Count(i => !string.IsNullOrWhiteSpace(i.Name)) > SkuaProtocol.MaxInventoryItems;
}
