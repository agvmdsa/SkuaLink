using AqwCompanion.Phone.Characters;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Phone;

public class CharacterBookTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset _now = T0;
    private readonly MemoryCharacterBookStore _store = new();

    private CharacterBook NewBook() => new(_store, () => _now);

    private static SkuaProfile Profile(long gold = 1200) => new(60, 340, 5000, gold, 777, 10, true, "Void Highlord", 31, 302500);

    private static SkuaStatus Status(string? character, SkuaProfile? profile = null, IReadOnlyList<SkuaFaction>? factions = null, bool loggedIn = true) =>
        new(loggedIn, character, "Artix", "battleon", null, false, null, [], factions ?? [], "idle", null, false, profile);

    private static SkuaInventory Inv(IReadOnlyList<string> read, params SkuaItem[] items) => new("Hero", read, items);

    [Fact]
    public void A_logged_in_status_records_the_character_its_profile_and_factions()
    {
        var book = NewBook();
        var raised = 0;
        book.Changed += (_, _) => raised++;
        book.OnStatus(Status("Hero", Profile(), [new SkuaFaction(1, "Nation", 3, 5000, 300, 1200)]));

        var hero = Assert.Single(book.All);
        Assert.Equal("Hero", hero.Name);
        Assert.Equal(Profile(), hero.Profile);
        Assert.Equal(T0, hero.ProfileAtUtc);
        Assert.Equal("Nation", Assert.Single(hero.Factions!).Name);
        Assert.Same(hero, book.Find("HERO"));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Logged_out_or_nameless_statuses_record_nothing()
    {
        var book = NewBook();
        book.OnStatus(Status("Hero", loggedIn: false));
        book.OnStatus(Status("  "));
        Assert.Empty(book.All);
    }

    [Fact]
    public void An_unchanged_status_raises_nothing_and_an_empty_faction_list_keeps_the_last_one()
    {
        var book = NewBook();
        book.OnStatus(Status("Hero", Profile(), [new SkuaFaction(1, "Nation", 3, 5000, 300, 1200)]));
        var raised = 0;
        book.Changed += (_, _) => raised++;
        _now = T0.AddSeconds(3);
        book.OnStatus(Status("Hero", Profile(), []));

        Assert.Equal(0, raised);
        Assert.Single(book.Find("Hero")!.Factions!);
        Assert.Equal(T0.AddSeconds(3), book.Find("Hero")!.LastSeenUtc);
    }

    [Fact]
    public void A_profile_change_counts_and_stamps_the_change()
    {
        var book = NewBook();
        book.OnStatus(Status("Hero", Profile(1200)));
        _now = T0.AddSeconds(9);
        book.OnStatus(Status("Hero", Profile(1300)));
        var hero = book.Find("Hero")!;
        Assert.Equal(1, hero.ProfileChanges);
        Assert.Equal(T0.AddSeconds(9), hero.ProfileChangedAtUtc);
    }

    [Fact]
    public void An_inventory_replaces_only_the_containers_it_read()
    {
        var book = NewBook();
        book.OnStatus(Status("Hero"));
        book.OnInventory("Hero", Inv(["inv", "house"], new SkuaItem("Sword", 1, "inv"), new SkuaItem("Statue", 1, "house")));
        book.OnInventory("hero", Inv(["inv", "bank"], new SkuaItem("Axe", 1, "inv"), new SkuaItem("Old Cape", 1, "bank")));

        var inventory = book.Find("Hero")!.Inventory!;
        Assert.Equal(["inv", "house", "bank"], inventory.Read);
        Assert.Equal(
            [new SkuaItem("Axe", 1, "inv"), new SkuaItem("Statue", 1, "house"), new SkuaItem("Old Cape", 1, "bank")],
            inventory.Items);
    }

    [Fact]
    public void An_identical_inventory_keeps_the_same_instance_and_raises_nothing()
    {
        var book = NewBook();
        book.OnInventory("Hero", Inv(["inv", "house"], new SkuaItem("Sword", 1, "inv")));
        var first = book.Find("Hero")!.Inventory;
        var raised = 0;
        book.Changed += (_, _) => raised++;
        book.OnInventory("Hero", Inv(["inv", "house"], new SkuaItem("Sword", 1, "inv")));
        Assert.Same(first, book.Find("Hero")!.Inventory);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void An_inventory_without_a_character_is_ignored()
    {
        var book = NewBook();
        book.OnInventory(null, Inv(["inv"], new SkuaItem("Sword", 1, "inv")));
        Assert.Empty(book.All);
    }

    [Fact]
    public void Saves_are_throttled_and_flush_writes_what_is_left()
    {
        var book = NewBook();
        book.OnStatus(Status("Hero", Profile(1)));
        Assert.Equal(1, _store.Saves);
        _now = T0.AddSeconds(10);
        book.OnStatus(Status("Hero", Profile(2)));
        Assert.Equal(1, _store.Saves);
        book.Flush();
        Assert.Equal(2, _store.Saves);
        Assert.Equal(2, _store.Stored.Single().Profile!.Gold);
    }

    [Fact]
    public void Restore_brings_back_the_stored_characters_and_live_ones_win()
    {
        _store.Stored = [new CharacterRecord("Hero", T0.AddDays(-1), Profile(5)), new CharacterRecord("Alt", T0.AddDays(-2), Profile(6))];
        var book = NewBook();
        book.OnStatus(Status("Hero", Profile(7)));
        book.Restore();
        Assert.Equal(["Alt", "Hero"], book.All.Select(r => r.Name));
        Assert.Equal(7, book.Find("Hero")!.Profile!.Gold);
    }

    [Fact]
    public void At_most_twenty_characters_are_kept_and_the_longest_unseen_goes_first()
    {
        var book = NewBook();
        for (var i = 0; i < CharacterBook.MaxCharacters + 1; i++)
        {
            _now = T0.AddMinutes(i);
            book.OnStatus(Status($"C{i:00}"));
        }

        Assert.Equal(CharacterBook.MaxCharacters, book.All.Count);
        Assert.Null(book.Find("C00"));
    }

    [Fact]
    public void Touch_stamps_when_the_character_was_last_seen()
    {
        var book = NewBook();
        book.OnStatus(Status("Hero"));
        _now = T0.AddMinutes(5);
        book.Touch("hero");
        Assert.Equal(T0.AddMinutes(5), book.Find("Hero")!.LastSeenUtc);
    }

    [Fact]
    public void Forget_clears_everything_and_a_quiesced_book_neither_records_nor_saves()
    {
        var book = NewBook();
        book.OnStatus(Status("Hero", Profile()));
        book.Quiesce();
        book.OnStatus(Status("Alt", Profile()));
        Assert.Null(book.Find("Alt"));
        var saves = _store.Saves;
        book.Flush();
        Assert.Equal(saves, _store.Saves);
        book.Forget();
        Assert.Empty(book.All);
        Assert.Equal(1, _store.Deletes);
        book.OnStatus(Status("Alt", Profile()));
        Assert.NotNull(book.Find("Alt"));
    }

    [Fact]
    public void A_handler_that_throws_never_stops_the_book()
    {
        var book = NewBook();
        book.Changed += (_, _) => throw new InvalidOperationException();
        book.OnStatus(Status("Hero"));
        Assert.Single(book.All);
    }
}
