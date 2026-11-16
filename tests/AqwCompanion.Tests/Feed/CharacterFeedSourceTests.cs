using AqwCompanion.App.Characters;
using AqwCompanion.App.Feed;
using AqwCompanion.App.HardFarms;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Feed;

public class CharacterFeedSourceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeSkua _skua = new();
    private readonly CharacterBook _book = new(new MemoryCharacterBookStore(), () => T0);

    private CharacterFeedSource NewSource() => new(
        _book, _skua, new CharacterSelection(_book, _skua),
        new HardFarmsViewModel(new HardFarmStore(new InMemoryStoredDataStore().Module()), new FakeHardFarmSync(), new FakeActiveInventory(), new FakeUrlLauncher()));

    [Fact]
    public void The_snapshot_is_cached_until_the_book_skua_selection_or_hard_farms_summary_change()
    {
        var source = NewSource();
        var raised = 0;
        source.Changed += (_, _) => raised++;
        var first = source.Current;
        Assert.Same(first, source.Current);
        _book.OnStatus(new SkuaStatus(true, "Hero", null, null, null, false, null, Profile: new SkuaProfile(1, 0, 0, 0, 0, 0, false)));
        Assert.NotSame(first, source.Current);
        var second = source.Current;
        _skua.Raise();
        Assert.NotSame(second, source.Current);
        Assert.True(raised >= 2);
    }

    [Fact]
    public void Dispose_stops_listening()
    {
        var source = NewSource();
        var raised = 0;
        source.Changed += (_, _) => raised++;
        source.Dispose();
        _skua.Raise();
        Assert.Equal(0, raised);
    }
}
