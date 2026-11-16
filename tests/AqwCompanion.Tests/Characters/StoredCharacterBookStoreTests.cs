using AqwCompanion.App.Characters;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Characters;

public class StoredCharacterBookStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryStoredDataStore _db = new();

    private StoredCharacterBookStore NewStore() => new(_db.Module());

    private static CharacterRecord Hero() => new(
        "Hero", T0, new SkuaProfile(60, 340, 5000, 1200, 777, 10, true, "Void Highlord", 31, 302500, "Heroes", 140, 300, 50), T0,
        [new SkuaFaction(1, "Nation", 3, 5000, 300, 1200)], T0,
        new SkuaInventory("Hero", ["inv", "house"], [new SkuaItem("Sword", 1, "inv"), new SkuaItem("Statue", 1, "house")]), T0,
        ProfileChangedAtUtc: T0, ProfileChanges: 4);

    [Fact]
    public void A_saved_book_loads_back_without_the_session_counters()
    {
        Assert.True(NewStore().Save([Hero()]));
        var loaded = Assert.Single(NewStore().Load());
        Assert.Equal(Hero() with { ProfileChangedAtUtc = null, ProfileChanges = 0, Factions = loaded.Factions, Inventory = loaded.Inventory }, loaded);
        Assert.Equal(Hero().Factions, loaded.Factions);
        Assert.Equal(Hero().Inventory, loaded.Inventory);
    }

    [Fact]
    public void Nothing_stored_or_a_failing_database_loads_as_empty()
    {
        Assert.Empty(NewStore().Load());
        _db.ThrowOnLoad = true;
        Assert.Empty(NewStore().Load());
    }

    [Fact]
    public void A_failing_save_answers_false()
    {
        _db.ThrowOnSave = true;
        Assert.False(NewStore().Save([Hero()]));
    }

    [Fact]
    public void Damaged_records_are_repaired_or_dropped_on_load()
    {
        _db.Items[StoredKeysName()] = (new StoredCharacterBookStore.BookDto(
        [
            new("", T0, null, null, null, null, null, null),
            new("Hero", T0, new(-1, -1, -1, -1, -1, -1, false, null, null, null, null, null, null, null), T0, null, null,
                new(["inv", "pocket"], [new("Sword", -3, "inv"), new("  ", 1, "inv"), new("Gem", 1, "pocket")], false), T0),
            new("hero", T0, null, null, null, null, null, null),
        ]), T0);

        var hero = Assert.Single(NewStore().Load());
        Assert.Equal(0, hero.Profile!.Level);
        Assert.Equal(["inv"], hero.Inventory!.Read);
        Assert.Equal([new SkuaItem("Sword", 0, "inv")], hero.Inventory.Items);
    }

    [Fact]
    public void Delete_removes_the_record()
    {
        NewStore().Save([Hero()]);
        NewStore().Delete();
        Assert.Empty(NewStore().Load());
    }

    private static string StoredKeysName() => AqwCompanion.App.Infrastructure.Persistence.StoredKeys.SkuaCharacters.Name;
}
