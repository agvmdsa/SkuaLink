using AqwCompanion.App;
using AqwCompanion.App.Characters;
using AqwCompanion.App.Debug;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Settings;
using AqwCompanion.App.Shell;
using AqwCompanion.Core.Diagnostics;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Characters;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>A <see cref="MainViewModel"/> over a character book, a fake Skua hub and fakes, with nothing stored or opened.</summary>
public static class MainViewModelBuilder
{
    public sealed record Built(
        MainViewModel Main, CharacterBook Book, FakeSkua Skua, FakeActiveInventory Inventory, FakeSettingsDialog Dialog,
        FakeDebugWindowHost DebugHost, FakeAppSettingsStore Settings, FakeHardFarmSync Sync, HardFarmStore Store);

    public static Built Create(Action<Action>? onUiThread = null, CharacterBook? book = null, FakeSkua? skua = null)
    {
        book ??= new CharacterBook(new MemoryCharacterBookStore(), () => DateTimeOffset.UtcNow);
        skua ??= new FakeSkua();
        var inventory = new FakeActiveInventory();
        var dialog = new FakeSettingsDialog();
        var debugHost = new FakeDebugWindowHost();
        var settingsStore = new FakeAppSettingsStore();
        var sync = new FakeHardFarmSync();
        var settings = new SettingsViewModel(settingsStore, new ForgetCoordinator(() => true, () => []));
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        var store = new HardFarmStore(new InMemoryStoredDataStore().Module());
        var main = new MainViewModel(
            new DiagnosticsViewModel(paths, new DiagnosticsExporter(new SnapshotRedactor())),
            new HardFarmsViewModel(store, sync, inventory, new FakeUrlLauncher()),
            new CharactersViewModel(book, skua, new CharacterSelection(book, skua), onUiThread),
            settings,
            dialog,
            new DebugModeController(settings, debugHost));
        return new Built(main, book, skua, inventory, dialog, debugHost, settingsStore, sync, store);
    }
}
