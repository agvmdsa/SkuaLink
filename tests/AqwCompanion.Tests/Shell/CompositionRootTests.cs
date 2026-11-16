using AqwCompanion.App;
using AqwCompanion.App.Characters;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Feed;
using AqwCompanion.App.Infrastructure;
using AqwCompanion.App.Infrastructure.AutoStart;
using AqwCompanion.App.Infrastructure.Persistence;
using AqwCompanion.App.Inventory;
using AqwCompanion.App.Phone;
using AqwCompanion.App.Push;
using AqwCompanion.App.Settings;
using AqwCompanion.App.Skua;
using AqwCompanion.App.Shell;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Core.Inventory;
using AqwCompanion.Core.Persistence;
using AqwCompanion.Core.Wiki;
using AqwCompanion.Phone.Characters;
using AqwCompanion.Phone.Devices;
using AqwCompanion.Phone.Hub;
using AqwCompanion.Phone.Push;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Phone.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AqwCompanion.Tests.Shell;

public class CompositionRootTests
{
    [Fact]
    public void Container_validates_and_resolves_the_main_view_model()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var main = services.GetRequiredService<MainViewModel>();
        Assert.Equal(AppInfo.ProductName, main.Title);
        Assert.NotNull(main.Diagnostics);
        Assert.NotNull(main.Characters);
    }

    [Fact]
    public void Container_resolves_the_hard_farm_pipeline_and_shares_the_view_model_with_the_shell()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.IsType<WikiPageFetcher>(services.GetRequiredService<IWikiPageFetcher>());
        Assert.IsType<HardFarmSync>(services.GetRequiredService<IHardFarmSync>());
        Assert.IsType<ProcessUrlLauncher>(services.GetRequiredService<IUrlLauncher>());
        Assert.Same(services.GetRequiredService<HardFarmsViewModel>(), services.GetRequiredService<MainViewModel>().HardFarms);
    }

    [Fact]
    public void Container_resolves_the_active_inventory_over_the_character_book()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var inventory = services.GetRequiredService<IActiveInventory>();
        Assert.IsType<ActiveInventory>(inventory);
        Assert.Null(inventory.Current);
    }

    [Fact]
    public void Container_resolves_the_character_selection_and_one_hard_farms_check()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.Null(services.GetRequiredService<CharacterSelection>().Selected);
        Assert.Same(services.GetRequiredService<HardFarmsCheck>(), services.GetRequiredService<HardFarmsCheck>());
    }

    [Fact]
    public void Container_resolves_the_settings_and_the_dialog_without_opening_a_window()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.IsType<JsonAppSettingsStore>(services.GetRequiredService<IAppSettingsStore>());
        Assert.IsType<AvaloniaSettingsDialog>(services.GetRequiredService<ISettingsDialog>());
        Assert.IsType<ScheduledTaskAutoStart>(services.GetRequiredService<IAutoStart>());
        Assert.Same(services.GetRequiredService<SettingsViewModel>(), services.GetRequiredService<MainViewModel>().Settings);
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void Container_resolves_the_local_feed_without_opening_a_port_or_writing_a_secret()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.IsType<HttpListenerFeedHost>(services.GetRequiredService<ILocalFeedHost>());
        Assert.False(services.GetRequiredService<ILocalFeedHost>().IsRunning);
        Assert.IsType<CharacterFeedSource>(services.GetRequiredService<AqwCompanion.Core.Feed.IFeedSource>());
        Assert.Equal(paths.FeedTokenPath, services.GetRequiredService<FeedSecretStore>().Path);
        Assert.Equal(paths.FeedTokenPath, services.GetRequiredService<SettingsViewModel>().FeedSecretPath);
        Assert.Equal("Off", services.GetRequiredService<LocalFeedController>().ListeningText is null ? "" : services.GetRequiredService<SettingsViewModel>().FeedStatusText);
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void Container_resolves_the_phone_access_without_opening_a_port_or_writing_a_file_and_forgets_through_the_coordinator()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var controller = services.GetRequiredService<PhoneController>();
        Assert.Same(controller, services.GetRequiredService<PhoneController>());
        Assert.NotNull(services.GetRequiredService<PhoneHub>());
        Assert.False(services.GetRequiredService<IPhoneHost>().IsRunning);
        Assert.Equal("Off", services.GetRequiredService<SettingsViewModel>().PhoneStatusText);
        Assert.True(services.GetRequiredService<SettingsViewModel>().HasPhone);
        Assert.False(File.Exists(paths.PhonePath));
        Assert.False(Directory.Exists(paths.Root));

        // It takes part in Forgetting: the coordinator revokes the paired devices of the registry the controller works on.
        var registry = services.GetRequiredService<PhoneRegistry>();
        Assert.Equal(PairStatus.Paired, registry.TryPair(registry.CreatePairingToken(), "Phone").Status);
        try
        {
            services.GetRequiredService<ForgetCoordinator>().Forget();
            Assert.Empty(registry.List());
        }
        finally
        {
            if (Directory.Exists(paths.Root))
            {
                Directory.Delete(paths.Root, recursive: true);
            }
        }
    }

    [Fact]
    public void The_phone_controller_is_the_last_forget_participant()
    {
        // PhoneController.Forget resets the hub, which reads the feed: every other participant must have forgotten first,
        // or the phone host comes back serving data from before the Forget.
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var participants = CompositionRoot.ForgetParticipants(services).ToList();
        Assert.True(participants.Count > 1);
        Assert.Same(services.GetRequiredService<PhoneController>(), participants[^1]);
        Assert.Single(participants, p => p is PhoneController);
        var book = participants.FindIndex(p => ReferenceEquals(p, services.GetRequiredService<CharacterBook>()));
        Assert.InRange(book, 0, participants.Count - 2);
    }

    [Fact]
    public void Container_resolves_the_character_book_over_the_stored_data()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.IsType<StoredCharacterBookStore>(services.GetRequiredService<ICharacterBookStore>());
        Assert.Empty(services.GetRequiredService<CharacterBook>().All);
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void The_skua_hub_is_shared_with_the_feed_and_the_command_channel_and_forgets_after_the_book_and_before_the_phone_controller()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var bridge = services.GetRequiredService<SkuaHub>();
        Assert.Same(bridge, services.GetRequiredService<ISkuaFeed>());
        Assert.Same(bridge, services.GetRequiredService<ISkuaCommandChannel>());
        Assert.NotNull(services.GetRequiredService<SkuaBridgeHost>());
        var participants = CompositionRoot.ForgetParticipants(services).ToList();
        var bridgeIndex = participants.FindIndex(p => ReferenceEquals(p, bridge));
        Assert.True(bridgeIndex >= 0);
        Assert.True(bridgeIndex < participants.FindIndex(p => p is PhoneController));
        // The book forgets first: a re-report arriving between the two would otherwise be dropped by the still-quiesced book,
        // and the plugin sends again only on a change.
        var bookIndex = participants.FindIndex(p => ReferenceEquals(p, services.GetRequiredService<CharacterBook>()));
        Assert.InRange(bookIndex, 0, bridgeIndex - 1);
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void The_skua_script_books_are_shared_with_the_phone_api_and_forget_before_the_phone_controller()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.NotNull(services.GetRequiredService<SkuaApi>());
        var participants = CompositionRoot.ForgetParticipants(services).ToList();
        var phone = participants.FindIndex(p => p is PhoneController);
        var prefs = participants.FindIndex(p => ReferenceEquals(p, services.GetRequiredService<SkuaScriptPrefs>()));
        var cache = participants.FindIndex(p => ReferenceEquals(p, services.GetRequiredService<SkuaOptionCache>()));
        Assert.InRange(prefs, 0, phone - 1);
        Assert.InRange(cache, 0, phone - 1);
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void Container_resolves_the_delay_seam()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.IsType<TaskDelay>(services.GetRequiredService<IDelay>());
    }

    [Fact]
    public void Container_resolves_the_persistence_services_without_creating_the_database_file()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.IsType<LiteDbStoredDataStore>(services.GetRequiredService<IStoredDataStore>());
        Assert.NotNull(services.GetRequiredService<StoredData>());
        Assert.Same(TimeProvider.System, services.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Container_resolves_the_application_exit()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        Assert.IsType<AvaloniaApplicationExit>(services.GetRequiredService<IApplicationExit>());
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void Container_resolves_the_tray_controller_over_the_window_host()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var controller = services.GetRequiredService<TrayController>();
        Assert.Same(controller, services.GetRequiredService<TrayController>());
        Assert.IsType<AvaloniaMainWindowHost>(services.GetRequiredService<IMainWindowHost>());
        Assert.Same(services.GetRequiredService<IMainWindowHost>(), services.GetRequiredService<AvaloniaMainWindowHost>());
        Assert.Equal("SkuaLink - Skua not connected", controller.ToolTip);
        Assert.False(controller.IsExiting);
    }

    [Fact]
    public void The_character_book_resolved_from_the_container_takes_part_in_forgetting()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        try
        {
            using var services = CompositionRoot.Build(paths);
            services.GetRequiredService<ICharacterBookStore>().Save([new CharacterRecord("Fictional Hero", DateTimeOffset.UnixEpoch)]);
            var book = services.GetRequiredService<CharacterBook>();
            book.Restore();
            Assert.Single(book.All);

            var result = services.GetRequiredService<ForgetCoordinator>().Forget();

            Assert.True(result.IsComplete);
            Assert.Empty(book.All);
            Assert.Empty(services.GetRequiredService<ICharacterBookStore>().Load());
        }
        finally
        {
            if (Directory.Exists(paths.Root))
            {
                Directory.Delete(paths.Root, recursive: true);
            }
        }
    }

    [Fact]
    public void App_info_exposes_non_empty_product_name_and_version()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.ProductName));
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
    }

    [Fact]
    public void The_push_stores_forget_before_the_phone_controller_and_building_creates_no_file()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));

        using var services = CompositionRoot.Build(paths);

        var participants = CompositionRoot.ForgetParticipants(services).ToList();
        var phone = participants.FindIndex(p => p is PhoneController);
        var subscriptions = participants.FindIndex(p => ReferenceEquals(p, services.GetRequiredService<PushSubscriptionStore>()));
        var key = participants.FindIndex(p => ReferenceEquals(p, services.GetRequiredService<VapidKeyStore>()));
        Assert.InRange(subscriptions, 0, phone - 1);
        Assert.InRange(key, 0, phone - 1);
        Assert.Same(services.GetRequiredService<VapidKeyStore>(), services.GetRequiredService<IVapidKeySource>());
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void A_revoked_device_loses_its_push_subscription_and_forget_removes_the_push_files()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        using var services = CompositionRoot.Build(paths);
        try
        {
            var registry = services.GetRequiredService<PhoneRegistry>();
            registry.TryPair(registry.CreatePairingToken(), "Phone");
            var id = registry.List().Single().Id;
            var store = services.GetRequiredService<PushSubscriptionStore>();
            store.Set(id, "https://fcm.googleapis.com/fcm/send/abc", "P", "A", null);
            services.GetRequiredService<VapidKeyStore>().GetOrCreate();
            Assert.True(File.Exists(paths.PushPath));
            Assert.True(File.Exists(paths.VapidKeyPath));

            registry.Revoke(id);
            Assert.Null(store.Get(id));

            store.Set("again", "https://fcm.googleapis.com/fcm/send/def", "P", "A", null);
            services.GetRequiredService<ForgetCoordinator>().Forget();

            Assert.False(File.Exists(paths.PushPath));
            Assert.False(File.Exists(paths.VapidKeyPath));
            Assert.Empty(store.All);
        }
        finally
        {
            if (Directory.Exists(paths.Root))
            {
                Directory.Delete(paths.Root, recursive: true);
            }
        }
    }

    [Fact]
    public void A_subscription_of_a_device_revoked_while_closed_is_dropped_when_the_store_is_built()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        try
        {
            Directory.CreateDirectory(paths.Root);
            new PushFileStore(paths.PushPath).Save(new PushFile(
                [new PushSubscription("gone", "https://fcm.googleapis.com/fcm/send/abc", "P", "A", PushKinds.All, DateTimeOffset.UtcNow, null)]));

            using var services = CompositionRoot.Build(paths);

            Assert.Empty(services.GetRequiredService<PushSubscriptionStore>().All);
            Assert.Empty(new PushFileStore(paths.PushPath).Load().Subscriptions);
        }
        finally
        {
            if (Directory.Exists(paths.Root))
            {
                Directory.Delete(paths.Root, recursive: true);
            }
        }
    }

    [Fact]
    public void The_push_pipeline_is_wired_hub_to_notifier_and_handler_to_api()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N")));
        try
        {
            using var services = CompositionRoot.Build(paths);

            Assert.NotNull(services.GetRequiredService<PhoneHub>());
            Assert.NotNull(services.GetRequiredService<PushNotifier>());
            Assert.NotNull(services.GetRequiredService<PushApi>());
            var handler = services.GetRequiredService<PhoneHandler>();
            var registry = services.GetRequiredService<PhoneRegistry>();
            Assert.False(Directory.Exists(paths.Root));
            var paired = registry.TryPair(registry.CreatePairingToken(), "Phone");
            Assert.Equal(PairStatus.Paired, paired.Status);
            var deviceId = registry.List().Single().Id;
            using var ua = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            var q = ua.ExportParameters(false).Q;
            services.GetRequiredService<PushSubscriptionStore>().Set(
                deviceId, "https://fcm.googleapis.com/fcm/send/abc",
                System.Buffers.Text.Base64Url.EncodeToString([0x04, .. q.X!, .. q.Y!]), System.Buffers.Text.Base64Url.EncodeToString(new byte[16]), null);

            var request = new PhoneRequest(
                "GET", "/api/push/state",
                new Dictionary<string, string> { ["Host"] = "127.0.0.1:47822", ["Cookie"] = $"{PhoneHandler.CookieName}={paired.DeviceToken}" }, string.Empty);
            var response = handler.Handle(request);

            Assert.Equal(200, response.Status);
            Assert.Contains("\"subscribed\":true", System.Text.Encoding.UTF8.GetString(response.Body), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(paths.Root))
            {
                Directory.Delete(paths.Root, true);
            }
        }
    }
}
