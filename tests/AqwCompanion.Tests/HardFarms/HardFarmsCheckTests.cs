using AqwCompanion.App.HardFarms;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.HardFarms;

public class HardFarmsCheckTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 1, 8, 30, 0, TimeSpan.Zero);

    private readonly HardFarmStore _store = new(new InMemoryStoredDataStore().Module());

    private static CharacterInventory Owns(params string[] names) =>
        new([.. names.Select(n => new InventoryItem(n, CharacterInventory.OtherTypeName, 1, false, false))]);

    private HardFarmsViewModel NewViewModel() => new(_store, new FakeHardFarmSync(), new FakeActiveInventory(), new FakeUrlLauncher());

    private async Task<HardFarmsViewModel> RestoredViewModelAsync(IReadOnlyList<string> slugs)
    {
        _store.SaveList(new HardFarmList([new HardFarmGroup("Nation", slugs)], null), At);
        var vm = NewViewModel();
        await vm.RestoreAsync();
        return vm;
    }

    [Fact]
    public async Task Check_matches_any_inventory_against_the_loaded_list_and_caches_per_inventory()
    {
        var vm = await RestoredViewModelAsync(["void-highlord-class", "cape-of-chaos"]);
        var check = new HardFarmsCheck(vm);
        var inventory = Owns("Void Highlord");
        var first = check.Check(inventory)!;
        Assert.Equal(1, first.Owned);
        Assert.Equal(2, first.Total);
        Assert.Same(first, check.Check(inventory));
    }

    [Fact]
    public void Without_a_list_check_answers_null()
    {
        var check = new HardFarmsCheck(NewViewModel());
        Assert.Null(check.Check(Owns("x")));
    }

    [Fact]
    public async Task A_new_list_raises_changed_and_drops_the_cache()
    {
        var vm = await RestoredViewModelAsync(["cape-of-chaos"]);
        var check = new HardFarmsCheck(vm);
        var raised = 0;
        check.Changed += (_, _) => raised++;
        vm.Forget();
        Assert.Equal(1, raised);
        Assert.Null(check.Check(Owns("Cape of Chaos")));
    }
}
