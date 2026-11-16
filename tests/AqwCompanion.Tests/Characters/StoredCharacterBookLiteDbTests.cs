using AqwCompanion.App.Characters;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Skua;

namespace AqwCompanion.Tests.Characters;

public sealed class StoredCharacterBookLiteDbTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aqw-characters-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private StoredCharacterBookStore Store() =>
        new(new StoredData(new LiteDbStoredDataStore(Path.Combine(_directory, "data", "companion.db")), StoredKeys.Obsolete));

    private CharacterBook Book() => new(Store(), () => T0);

    [Fact]
    public void A_book_round_trips_through_the_real_database_and_forget_deletes_it()
    {
        var profile = new SkuaProfile(60, 340, 5000, 1200, 777, 10, true, "Void Highlord", 31, 302500, "Heroes", 140, 300, 50);
        SkuaFaction[] factions = [new(1, "Nation", 3, 5000, 300, 1200), new(2, "Good", 10, 302500, 0, 0)];
        var inventory = new SkuaInventory(
            "Hero", ["inv", "bank", "house"],
            [new SkuaItem("Sword", 1, "inv"), new SkuaItem("Gem", 250, "inv"), new SkuaItem("Old Cape", 1, "bank"), new SkuaItem("Statue", 2, "house")],
            Truncated: true);
        using (var book = Book())
        {
            book.OnStatus(new SkuaStatus(true, "Hero", "Artix", "battleon", null, false, null, [], factions, "idle", null, false, profile));
            book.OnInventory("Hero", inventory);
        }

        var again = Book();
        again.Restore();
        var hero = Assert.Single(again.All);
        Assert.Equal("Hero", hero.Name);
        Assert.Equal(T0, hero.LastSeenUtc);
        Assert.Equal(profile, hero.Profile);
        Assert.Equal(T0, hero.ProfileAtUtc);
        Assert.Equal(factions, hero.Factions);
        Assert.Equal(T0, hero.FactionsAtUtc);
        Assert.Equal(inventory, hero.Inventory);
        Assert.True(hero.Inventory!.Truncated);
        Assert.Equal(T0, hero.InventoryAtUtc);

        again.Forget();
        Assert.Empty(Store().Load());
        Assert.Null(new StoredData(new LiteDbStoredDataStore(Path.Combine(_directory, "data", "companion.db")), StoredKeys.Obsolete)
            .Load(StoredKeys.SkuaCharacters));
    }
}
