using AqwCompanion.App.Settings;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Settings;

public class SettingsUpdatesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static SettingsViewModel Vm(FakeAppSettingsStore store) => new(store, new ForgetCoordinator(() => true, () => []));

    [Fact]
    public void Checking_for_updates_is_on_by_default_and_nothing_is_announced()
    {
        var vm = Vm(new FakeAppSettingsStore());

        Assert.True(vm.CheckForUpdatesEnabled);
        Assert.Equal(string.Empty, vm.AnnouncedUpdate);
    }

    [Fact]
    public void The_switch_is_saved_and_raises_the_event_only_on_a_real_change()
    {
        var store = new FakeAppSettingsStore();
        var vm = Vm(store);
        var raised = 0;
        vm.CheckForUpdatesChanged += (_, _) => raised++;

        vm.CheckForUpdatesEnabled = true;
        vm.CheckForUpdatesEnabled = false;
        vm.CheckForUpdatesEnabled = false;

        Assert.Equal(1, raised);
        Assert.False(store.Current.CheckForUpdates);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void The_announced_version_is_saved_once_and_keeps_the_other_settings()
    {
        var store = new FakeAppSettingsStore(new AppSettings(DebugMode: true));
        var vm = Vm(store);

        vm.RememberAnnouncedUpdate("0.8.0");
        vm.RememberAnnouncedUpdate("0.8.0");

        Assert.Equal("0.8.0", vm.AnnouncedUpdate);
        Assert.Equal("0.8.0", store.Current.AnnouncedUpdate);
        Assert.True(store.Current.DebugMode);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void A_settings_file_of_an_older_version_loads_with_the_new_defaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{"debugMode":true,"localFeed":false,"phoneAccess":true,"autoStartMigrated":true}""");

        var loaded = new JsonAppSettingsStore(path).Load();

        Assert.True(loaded.DebugMode);
        Assert.True(loaded.PhoneAccess);
        Assert.True(loaded.CheckForUpdates);
        Assert.Equal(string.Empty, loaded.AnnouncedUpdate);
    }

    [Fact]
    public void The_new_settings_round_trip_through_the_file()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new JsonAppSettingsStore(path);

        store.Save(new AppSettings(CheckForUpdates: false, AnnouncedUpdate: "0.9.1"));

        var loaded = new JsonAppSettingsStore(path).Load();
        Assert.False(loaded.CheckForUpdates);
        Assert.Equal("0.9.1", loaded.AnnouncedUpdate);
    }

    [Fact]
    public void A_null_announced_version_in_the_file_reads_as_none()
    {
        var path = Path.Combine(_dir, "settings.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{"announcedUpdate":null}""");

        var vm = Vm(new FakeAppSettingsStore(new JsonAppSettingsStore(path).Load()));

        Assert.Equal(string.Empty, vm.AnnouncedUpdate);
    }
}
