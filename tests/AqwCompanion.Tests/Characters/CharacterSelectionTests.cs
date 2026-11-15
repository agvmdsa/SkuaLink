using AqwCompanion.App.Characters;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Characters;

public class CharacterSelectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset _now = T0;
    private readonly FakeSkua _skua = new();
    private readonly CharacterBook _book;

    public CharacterSelectionTests() => _book = new CharacterBook(new MemoryCharacterBookStore(), () => _now);

    private void Seen(string name, int minutes)
    {
        _now = T0.AddMinutes(minutes);
        _book.OnStatus(new SkuaStatus(true, name, null, null, null, false, null));
    }

    private void Live(string id, string character) =>
        _skua.Current = new SkuaHubView([.. _skua.Current.Instances, FakeSkua.Instance(id, character, status: new SkuaStatus(true, character, null, null, null, false, null))]);

    [Fact]
    public void Nothing_known_selects_nothing()
    {
        Assert.Null(new CharacterSelection(_book, _skua).Selected);
    }

    [Fact]
    public void The_first_live_character_wins_over_the_most_recently_seen()
    {
        Seen("Alt", 1);
        Seen("Hero", 2);
        var selection = new CharacterSelection(_book, _skua);
        Assert.Equal("Hero", selection.Selected);
        Live("a", "Alt");
        _skua.Raise();
        Assert.Equal("Alt", selection.Selected);
    }

    [Fact]
    public void A_chosen_character_stays_chosen_while_the_book_knows_it()
    {
        Seen("Alt", 1);
        Seen("Hero", 2);
        var selection = new CharacterSelection(_book, _skua);
        var raised = 0;
        selection.Changed += (_, _) => raised++;
        selection.Choose("alt");
        Assert.Equal("Alt", selection.Selected);
        Assert.Equal(1, raised);
        selection.Choose("Nobody");
        Assert.Equal("Hero", selection.Selected);
    }

    [Fact]
    public void Changed_is_raised_only_when_the_selection_changes()
    {
        Seen("Hero", 1);
        var selection = new CharacterSelection(_book, _skua);
        var raised = 0;
        selection.Changed += (_, _) => raised++;
        Seen("Hero", 2);
        _skua.Raise();
        Assert.Equal(0, raised);
    }
}
