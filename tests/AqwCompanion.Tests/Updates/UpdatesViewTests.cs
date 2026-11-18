using AqwCompanion.App.Settings;
using AqwCompanion.App.Shell;
using AqwCompanion.App.Updates;
using AqwCompanion.Tests.TestDoubles;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AqwCompanion.Tests.Updates;

public class UpdatesViewTests
{
    private static (MainWindow Window, UpdatesRig Rig) MainRig()
    {
        var built = MainViewModelBuilder.Create();
        var rig = new UpdatesRig(settings: built.Main.Settings);
        var window = new MainWindow { DataContext = built.Main };
        window.Show();
        return (window, rig);
    }

    private static (SettingsWindow Window, UpdatesRig Rig) Settings(bool withUpdates = true)
    {
        var rig = new UpdatesRig();
        var vm = withUpdates ? rig.Settings : new SettingsViewModel(new FakeAppSettingsStore(), new AqwCompanion.Core.Forgetting.ForgetCoordinator(() => true, () => []));
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        return (window, rig);
    }

    [AvaloniaFact]
    public void The_banner_is_hidden_without_an_update_and_without_the_view_model()
    {
        var built = MainViewModelBuilder.Create();
        var window = new MainWindow { DataContext = built.Main };
        window.Show();

        Assert.False(window.FindControl<Border>("UpdateBanner")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_banner_shows_the_text_and_the_buttons_when_an_update_is_available_and_hides_when_dismissed()
    {
        var (window, rig) = MainRig();
        using (rig)
        {
            var banner = window.FindControl<Border>("UpdateBanner")!;
            Assert.False(banner.IsEffectivelyVisible);

            await rig.CheckAsync();

            Assert.True(banner.IsEffectivelyVisible);
            Assert.Equal("SkuaLink 0.8.0 is available", window.FindControl<TextBlock>("UpdateBannerText")!.Text);
            Assert.Equal(UpdatesViewModel.UpdateNowText, (string?)window.FindControl<Button>("UpdateBannerUpdate")!.Content);
            Assert.Equal(UpdatesViewModel.ReleaseNotesText, (string?)window.FindControl<Button>("UpdateBannerNotes")!.Content);
            Assert.Same(rig.Vm.UpdateNowCommand, window.FindControl<Button>("UpdateBannerUpdate")!.Command);
            Assert.False(window.FindControl<Button>("UpdateBannerCancel")!.IsEffectivelyVisible);

            window.FindControl<Button>("UpdateBannerDismiss")!.Command!.Execute(null);

            Assert.False(banner.IsEffectivelyVisible);
        }
    }

    [AvaloniaFact]
    public async Task The_banner_carries_the_new_dll_part_with_the_download_button()
    {
        var (window, rig) = MainRig();
        using (rig)
        {
            await rig.CheckAsync();

            var part = window.FindControl<PluginRequirementView>("UpdateBannerPlugin")!;
            Assert.True(part.IsEffectivelyVisible);
            Assert.StartsWith(UpdatesViewModel.NewDllRequiredText, part.FindControl<TextBlock>("PluginRequirementText")!.Text, StringComparison.Ordinal);
            var button = part.FindControl<Button>("DownloadPluginButton")!;
            Assert.Equal(UpdatesViewModel.DownloadPluginText, (string?)button.Content);
            Assert.Same(rig.Vm.DownloadPluginCommand, button.Command);
        }
    }

    [AvaloniaFact]
    public void The_plugin_part_is_hidden_when_nothing_is_required()
    {
        var (window, rig) = MainRig();
        using (rig)
        {
            Assert.False(window.FindControl<PluginRequirementView>("UpdateBannerPlugin")!.IsEffectivelyVisible);
        }
    }

    [AvaloniaFact]
    public void The_updates_block_is_hidden_when_the_app_wired_no_updates()
    {
        var (window, rig) = Settings(withUpdates: false);
        using (rig)
        {
            Assert.False(window.FindControl<Border>("SettingsUpdatesSection")!.IsEffectivelyVisible);
            Assert.False(window.FindControl<PluginRequirementView>("SettingsSkuaPlugin")!.IsEffectivelyVisible);
        }
    }

    [AvaloniaFact]
    public async Task The_updates_block_shows_the_versions_the_last_check_and_the_buttons()
    {
        var (window, rig) = Settings();
        using (rig)
        {
            Assert.True(window.FindControl<Border>("SettingsUpdatesSection")!.IsEffectivelyVisible);
            Assert.Equal("Current version 0.7.0", window.FindControl<TextBlock>("SettingsUpdatesCurrent")!.Text);
            Assert.Equal("Not checked yet.", window.FindControl<TextBlock>("SettingsUpdatesLast")!.Text);

            window.FindControl<Button>("SettingsUpdatesCheckNow")!.Command!.Execute(null);
            await Task.Delay(50);
            SpinWait.SpinUntil(() => rig.Vm.IsUpdateAvailable, TimeSpan.FromSeconds(5));

            Assert.Equal("Latest version 0.8.0", window.FindControl<TextBlock>("SettingsUpdatesLatest")!.Text);
            Assert.Same(rig.Vm.UpdateNowCommand, window.FindControl<Button>("SettingsUpdatesUpdateNow")!.Command);
            Assert.Equal(UpdatesViewModel.CheckNowText, (string?)window.FindControl<Button>("SettingsUpdatesCheckNow")!.Content);
            Assert.True(window.FindControl<PluginRequirementView>("SettingsUpdatesPlugin")!.IsEffectivelyVisible);
            Assert.True(window.FindControl<PluginRequirementView>("SettingsSkuaPlugin")!.IsEffectivelyVisible);
        }
    }

    [AvaloniaFact]
    public void The_check_switch_follows_and_changes_the_setting()
    {
        var (window, rig) = Settings();
        using (rig)
        {
            var toggle = window.FindControl<ToggleSwitch>("SettingsUpdatesSwitch")!;
            Assert.True(toggle.IsChecked);

            toggle.IsChecked = false;

            Assert.False(rig.Settings.CheckForUpdatesEnabled);
            Assert.False(rig.Vm.CheckForUpdates);
        }
    }
}
