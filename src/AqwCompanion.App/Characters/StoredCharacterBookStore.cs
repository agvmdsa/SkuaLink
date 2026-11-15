using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Skua;

namespace AqwCompanion.App.Characters;

/// <summary>
/// Keeps the <see cref="CharacterBook"/> as one stored value of plain DTOs and validates it on load (the session counters are
/// never stored). Failures never reach the caller: the stored data module swallows and reports them.
/// </summary>
public sealed class StoredCharacterBookStore(StoredData data) : ICharacterBookStore
{
    public sealed record BookDto(List<CharacterDto> Characters);

    public sealed record CharacterDto(string Name, DateTimeOffset LastSeenUtc, ProfileDto? Profile, DateTimeOffset? ProfileAtUtc,
        List<FactionDto>? Factions, DateTimeOffset? FactionsAtUtc, InventoryDto? Inventory, DateTimeOffset? InventoryAtUtc);

    public sealed record ProfileDto(int Level, long Xp, long XpToLevel, long Gold, int Coins, int ClassRank, bool IsMember,
        string? ClassName, int? ClassId, int? ClassPoints, string? Guild, int? BagSlots, int? BankSlots, int? HouseSlots);

    public sealed record FactionDto(int Id, string Name, int Rank, int TotalRep, int Rep, int RequiredRep);

    public sealed record InventoryDto(List<string> Read, List<ItemDto> Items, bool Truncated);

    public sealed record ItemDto(string Name, int Qty, string Where);

    public IReadOnlyList<CharacterRecord> Load()
    {
        if (data.Load(StoredKeys.SkuaCharacters)?.Value?.Characters is not { } dtos)
        {
            return [];
        }

        var records = new List<CharacterRecord>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Name))
            {
                continue;
            }

            var name = SkuaText.Cut(dto.Name.Trim(), SkuaProtocol.MaxTextLength);
            if (!names.Add(name))
            {
                continue;
            }

            records.Add(new CharacterRecord(
                name, dto.LastSeenUtc,
                ToProfile(dto.Profile), dto.Profile is null ? null : dto.ProfileAtUtc,
                ToFactions(dto.Factions), dto.Factions is null ? null : dto.FactionsAtUtc,
                ToInventory(name, dto.Inventory), dto.Inventory is null ? null : dto.InventoryAtUtc));
        }

        if (records.Count <= CharacterBook.MaxCharacters)
        {
            return records;
        }

        var kept = records.OrderByDescending(r => r.LastSeenUtc).Take(CharacterBook.MaxCharacters).ToHashSet();
        return [.. records.Where(kept.Contains)];
    }

    public bool Save(IReadOnlyList<CharacterRecord> records) =>
        data.Save(StoredKeys.SkuaCharacters, new BookDto([.. records.Select(ToDto)]), DateTimeOffset.UtcNow);

    public void Delete() => data.Remove(StoredKeys.SkuaCharacters);

    private static CharacterDto ToDto(CharacterRecord r) => new(
        r.Name, r.LastSeenUtc,
        r.Profile is { } p
            ? new ProfileDto(p.Level, p.Xp, p.XpToLevel, p.Gold, p.Coins, p.ClassRank, p.IsMember, p.ClassName, p.ClassId, p.ClassPoints,
                p.Guild, p.BagSlots, p.BankSlots, p.HouseSlots)
            : null,
        r.ProfileAtUtc,
        r.Factions?.Select(f => new FactionDto(f.Id, f.Name, f.Rank, f.TotalRep, f.Rep, f.RequiredRep)).ToList(),
        r.FactionsAtUtc,
        r.Inventory is { } i
            ? new InventoryDto([.. i.Read], [.. i.Items.Select(item => new ItemDto(item.Name, item.Quantity, item.Where))], i.Truncated)
            : null,
        r.InventoryAtUtc);

    private static SkuaProfile? ToProfile(ProfileDto? p) => p is null
        ? null
        : new SkuaProfile(
            Math.Max(0, p.Level), Math.Max(0, p.Xp), Math.Max(0, p.XpToLevel), Math.Max(0, p.Gold), Math.Max(0, p.Coins),
            Math.Max(0, p.ClassRank), p.IsMember, Text(p.ClassName), Positive(p.ClassId), p.ClassPoints is >= 0 ? p.ClassPoints : null,
            Text(p.Guild), Positive(p.BagSlots), Positive(p.BankSlots), Positive(p.HouseSlots));

    private static List<SkuaFaction>? ToFactions(List<FactionDto>? factions) => factions?
        .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Name)
            && f.Id >= 0 && f.Rank >= 0 && f.TotalRep >= 0 && f.Rep >= 0 && f.RequiredRep >= 0)
        .Take(SkuaProtocol.MaxFactions)
        .Select(f => new SkuaFaction(f.Id, SkuaText.Cut(f.Name.Trim(), SkuaProtocol.MaxTextLength), f.Rank, f.TotalRep, f.Rep, f.RequiredRep))
        .ToList();

    private static SkuaInventory? ToInventory(string character, InventoryDto? inventory)
    {
        if (inventory is null)
        {
            return null;
        }

        var read = (inventory.Read ?? []).Where(c => c is not null && SkuaProtocol.InventoryContainers.Contains(c)).Distinct().ToList();
        var perContainer = new Dictionary<string, int>(StringComparer.Ordinal);
        var items = new List<SkuaItem>();
        foreach (var i in inventory.Items ?? [])
        {
            if (i is null || string.IsNullOrWhiteSpace(i.Name) || i.Where is null || !read.Contains(i.Where))
            {
                continue;
            }

            var count = perContainer.GetValueOrDefault(i.Where);
            if (count >= SkuaProtocol.MaxInventoryItems)
            {
                continue;
            }

            perContainer[i.Where] = count + 1;
            items.Add(new SkuaItem(SkuaText.Cut(i.Name.Trim(), SkuaProtocol.MaxTextLength), Math.Max(0, i.Qty), i.Where));
        }

        return new SkuaInventory(character, read, items, inventory.Truncated);
    }

    private static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : SkuaText.Cut(value.Trim(), SkuaProtocol.MaxTextLength);

    private static int? Positive(int? value) => value is > 0 ? value : null;
}
