namespace AqwCompanion.Phone.Push;

/// <summary>The kinds of push notification a phone can choose.</summary>
public static class PushKinds
{
    private static readonly string[] Names =
    [
        "ScriptStopped", "ScriptError", "SkuaDisconnected", "CharacterKicked", "SkuaOptionsWaiting",
        "RankUp", "HardFarmDrop", "NoProgress", "DyingOften",
    ];

    private static readonly HashSet<string> Known = new(Names, StringComparer.Ordinal);

    public static IReadOnlyList<string> All { get; } = Names;

    /// <summary>The distinct known kinds in <see cref="All"/> order; null when the input is null or holds an unknown, null or empty entry.</summary>
    public static IReadOnlyList<string>? Normalize(IEnumerable<string?>? kinds)
    {
        if (kinds is null)
        {
            return null;
        }

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in kinds)
        {
            if (kind is null || !Known.Contains(kind))
            {
                return null;
            }

            wanted.Add(kind);
        }

        return Names.Where(wanted.Contains).ToList();
    }

    internal static bool IsKnown(string? kind) => kind is not null && Known.Contains(kind);
}
