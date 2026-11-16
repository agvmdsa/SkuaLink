using AqwCompanion.App.Characters;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Characters;

public class CharactersViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly CharacterBook _book = new(new MemoryCharacterBookStore(), () => T0);
    private readonly FakeSkua _skua = new();

    private CharactersViewModel NewVm() => new(_book, _skua, new CharacterSelection(_book, _skua));

    private void Report(string name, SkuaProfile? profile = null, params SkuaFaction[] factions) =>
        _book.OnStatus(new SkuaStatus(true, name, null, null, null, false, null, [], factions, null, null, false, profile));

    [Fact]
    public void Without_characters_it_shows_the_empty_state_and_skua_not_connected()
    {
        var vm = NewVm();
        Assert.False(vm.HasCharacters);
        Assert.Equal(CharactersViewModel.NoCharacterText, vm.CharacterChipText);
        Assert.Equal("?", vm.SelectedCharacterInitial);
        Assert.Equal("Skua not connected.", vm.FooterText);
        Assert.False(vm.IsLive);
    }

    [Fact]
    public void The_selected_character_shows_its_numbers_class_and_factions_closest_first()
    {
        Report("Hero", new SkuaProfile(60, 340, 5000, 1200, 777, 10, true, "Void Highlord", 31, 302500, "Heroes", 140, 300, 50),
            new SkuaFaction(1, "Good", 10, 302500, 0, 0), new SkuaFaction(2, "Nation", 3, 5000, 300, 1200));
        var vm = NewVm();

        Assert.Equal("Hero · Level 60 · Void Highlord rank 10", vm.ProfileHeadline);
        Assert.Equal(
            ["Gold: 1,200", "Exp: 340 / 5,000", "AC: 777", "Class points: 302,500", "Membership: Member", "Guild: Heroes", "Slots: 140 bag, 300 bank, 50 house"],
            vm.ProfileFigures.Select(f => f.Label + ": " + f.Value));
        Assert.Equal(["Nation", "Good"], vm.Factions.Select(f => f.Name));
        Assert.Equal("H", vm.SelectedCharacterInitial);
    }

    [Fact]
    public void A_remembered_character_says_when_it_was_last_seen_and_a_live_one_says_live()
    {
        Report("Hero", new SkuaProfile(1, 0, 0, 0, 0, 0, false));
        var vm = NewVm();
        Assert.StartsWith("Last seen ", vm.UpdatedText);
        _skua.Current = new SkuaHubView([FakeSkua.Instance("a", "Hero", status: new SkuaStatus(true, "Hero", null, null, null, false, null))]);
        _skua.Raise();
        Assert.Equal(CharactersViewModel.LiveText, vm.UpdatedText);
        Assert.True(vm.IsLive);
        Assert.Equal("1 Skua connected.", vm.FooterText);
    }

    [Fact]
    public void A_character_without_numbers_says_the_plugin_must_be_updated()
    {
        Report("Hero");
        Assert.Equal(CharactersViewModel.NoProfileText, NewVm().ProfileHeadline);
    }

    [Fact]
    public void Choosing_a_character_in_the_chip_selects_it()
    {
        Report("Alt", new SkuaProfile(1, 0, 0, 0, 0, 0, false));
        Report("Hero", new SkuaProfile(2, 0, 0, 0, 0, 0, false));
        var vm = NewVm();
        vm.SelectedCharacter = "Alt";
        Assert.Equal("Alt", vm.CharacterChipText);
        Assert.StartsWith("Alt · Level 1", vm.ProfileHeadline);
    }

    [Fact]
    public void Changes_from_other_threads_are_refreshed_through_the_ui_callback_once_per_burst()
    {
        var queued = new List<Action>();
        var vm = new CharactersViewModel(_book, _skua, new CharacterSelection(_book, _skua), queued.Add);
        Report("Hero", new SkuaProfile(1, 0, 0, 0, 0, 0, false));
        _skua.Raise();
        Assert.Single(queued);
        queued[0]();
        Assert.True(vm.HasCharacters);
    }

    [Fact]
    public void The_name_list_stays_the_same_object_while_the_names_do_not_change()
    {
        Report("Hero", new SkuaProfile(1, 0, 0, 0, 0, 0, false));
        var vm = NewVm();
        var names = vm.CharacterNames;

        Report("Hero", new SkuaProfile(2, 0, 0, 0, 0, 0, false));
        Assert.Same(names, vm.CharacterNames);

        Report("Alt", new SkuaProfile(1, 0, 0, 0, 0, 0, false));
        Assert.Equal(["Alt", "Hero"], vm.CharacterNames);
    }
}
