namespace AqwCompanion.Skua;

/// <summary>Which bag items are boosts and which type each one is. Pure.</summary>
public static class SkuaBoostItems
{
    private static readonly string[] Types = ["gold", "class", "rep", "xp"];

    /// <summary>"gold", "class", "rep" or "xp" when the name contains exactly one of those words (case-insensitive); otherwise "unknown".</summary>
    public static string TypeOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unknown";
        }

        string? found = null;
        foreach (var type in Types)
        {
            if (name.Contains(type, StringComparison.OrdinalIgnoreCase))
            {
                if (found is not null)
                {
                    return "unknown";
                }

                found = type;
            }
        }

        return found ?? "unknown";
    }

    public static bool IsBoostName(string? name) => name?.Contains("boost", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The boost items of a bag: ServerUse items whose name says boost, with an id and a quantity; sorted; at most <see cref="SkuaProtocol.MaxBoostItems"/>.</summary>
    public static IReadOnlyList<SkuaBoostItem> Build(IEnumerable<(int Id, string? Name, int Quantity, bool ServerUse)> bag)
    {
        var items = new List<SkuaBoostItem>();
        var seen = new HashSet<int>();
        foreach (var (id, rawName, quantity, serverUse) in bag)
        {
            var name = rawName?.Trim();
            if (!serverUse || id <= 0 || quantity <= 0 || !IsBoostName(name) || !seen.Add(id))
            {
                continue;
            }

            name = SkuaText.Cut(name!, SkuaProtocol.MaxBoostNameLength);
            items.Add(new SkuaBoostItem(id, name, TypeOf(name), quantity));
        }

        return [.. items
            .OrderBy(i => Array.IndexOf(Types, i.Type) is var at && at < 0 ? Types.Length : at)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.ItemId)
            .Take(SkuaProtocol.MaxBoostItems)];
    }
}
