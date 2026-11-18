using AqwCompanion.App.Updates;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Updates;

public class UpdatesViewModelTests
{
    [Fact]
    public void Before_any_check_the_texts_say_so_and_nothing_is_offered()
    {
        using var rig = new UpdatesRig();

        Assert.Equal("Current version 0.7.0", rig.Vm.CurrentVersionText);
        Assert.Equal("Latest version unknown", rig.Vm.LatestVersionText);
        Assert.Equal("Not checked yet.", rig.Vm.LastCheckText);
        Assert.False(rig.Vm.IsUpdateAvailable);
        Assert.False(rig.Vm.IsBannerVisible);
        Assert.False(rig.Vm.HasPluginRequirement);
        Assert.Same(rig.Vm, rig.Settings.Updates);
        Assert.True(rig.Settings.HasUpdates);
        Assert.True(rig.Vm.CheckForUpdates);
    }

    [Fact]
    public async Task A_newer_release_shows_the_banner_the_versions_and_the_check_result()
    {
        using var rig = new UpdatesRig();

        await rig.CheckAsync();

        Assert.True(rig.Vm.IsUpdateAvailable);
        Assert.True(rig.Vm.IsBannerVisible);
        Assert.Equal("SkuaLink 0.8.0 is available", rig.Vm.BannerText);
        Assert.Equal("Latest version 0.8.0", rig.Vm.LatestVersionText);
        Assert.StartsWith("Last check ", rig.Vm.LastCheckText, StringComparison.Ordinal);
        Assert.EndsWith("update available.", rig.Vm.LastCheckText, StringComparison.Ordinal);
        Assert.True(rig.Vm.UpdateNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_same_version_says_up_to_date_and_a_failed_check_says_could_not_check()
    {
        using var same = new UpdatesRig(released: "0.7.0");
        await same.CheckAsync();
        Assert.EndsWith("up to date.", same.Vm.LastCheckText, StringComparison.Ordinal);
        Assert.False(same.Vm.IsBannerVisible);
        Assert.False(same.Vm.UpdateNowCommand.CanExecute(null));

        using var failing = new UpdatesRig();
        failing.Releases.Default = new ReleaseCheck(ReleaseCheckStatus.Failed);
        await failing.CheckAsync();
        Assert.StartsWith("Could not check", failing.Vm.LastCheckText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dismissing_hides_the_banner_for_that_version_only()
    {
        using var rig = new UpdatesRig();
        await rig.CheckAsync();

        rig.Vm.DismissBannerCommand.Execute(null);

        Assert.False(rig.Vm.IsBannerVisible);
        Assert.True(rig.Vm.IsUpdateAvailable);

        rig.Releases.Default = FakeGitHubReleases.Found("0.9.0", "\"e2\"");
        await rig.CheckAsync();

        Assert.True(rig.Vm.IsBannerVisible);
        Assert.Equal("SkuaLink 0.9.0 is available", rig.Vm.BannerText);
    }

    [Fact]
    public async Task Check_now_runs_a_check()
    {
        using var rig = new UpdatesRig();

        await rig.Vm.CheckNowCommand.ExecuteAsync(null);

        Assert.Single(rig.Releases.Etags);
        Assert.True(rig.Vm.IsUpdateAvailable);
    }

    [Fact]
    public async Task The_switch_off_hides_everything_and_asks_nothing_and_on_checks_at_once()
    {
        using var rig = new UpdatesRig();
        await rig.CheckAsync();

        rig.Vm.CheckForUpdates = false;

        Assert.False(rig.Settings.CheckForUpdatesEnabled);
        Assert.False(rig.Vm.IsBannerVisible);
        Assert.Equal("Checking for updates is off.", rig.Vm.LastCheckText);
        Assert.Single(rig.Releases.Etags);

        rig.Vm.CheckForUpdates = true;

        Assert.True(SpinWait.SpinUntil(() => rig.Vm.IsBannerVisible, TimeSpan.FromSeconds(5)));
        Assert.Equal(2, rig.Releases.Etags.Count);
    }

    [Fact]
    public async Task Changes_from_the_checker_are_applied_on_the_ui_thread()
    {
        var posted = new List<Action>();
        using var rig = new UpdatesRig(onUiThread: posted.Add);

        await rig.CheckAsync();

        Assert.False(rig.Vm.IsUpdateAvailable);
        foreach (var action in posted.ToList())
        {
            action();
        }

        Assert.True(rig.Vm.IsUpdateAvailable);
    }

    [Fact]
    public async Task Update_now_downloads_verifies_starts_the_installer_and_exits_once()
    {
        using var rig = new UpdatesRig();
        await rig.CheckAsync();

        await rig.Vm.UpdateNowCommand.ExecuteAsync(null);

        var launched = Assert.Single(rig.Launcher.Launched);
        Assert.True(rig.Launcher.InstallerExistedAtLaunch);
        Assert.Equal(rig.InstallerBytes, await File.ReadAllBytesAsync(launched.Path));
        Assert.Equal(1, rig.Exit.ExitCalls);
        Assert.Equal(UpdatesViewModel.UpdateStartedText, rig.Vm.StatusText);
        Assert.True(Directory.Exists(launched.Folder));
        Assert.False(rig.Vm.IsBusy);
    }

    [Theory]
    [InlineData(LaunchStatus.Cancelled, "UacCancelledText")]
    [InlineData(LaunchStatus.Failed, "LaunchFailedText")]
    public async Task A_cancelled_windows_prompt_or_a_failed_start_keeps_skualink_running_and_deletes_the_download(LaunchStatus status, string text)
    {
        using var rig = new UpdatesRig();
        rig.Launcher.Result = status;
        await rig.CheckAsync();

        await rig.Vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(0, rig.Exit.ExitCalls);
        Assert.Equal(text == "UacCancelledText" ? UpdatesViewModel.UacCancelledText : UpdatesViewModel.LaunchFailedText, rig.Vm.StatusText);
        Assert.Empty(rig.TempFolders());
        Assert.False(rig.Vm.IsBusy);
    }

    [Fact]
    public async Task A_checksum_mismatch_runs_nothing_and_deletes_the_download()
    {
        var wrong = $"{new string('0', 64)}  SkuaLink-Setup-v0.8.0.exe\n";
        using var rig = new UpdatesRig(sums: wrong);
        await rig.CheckAsync();

        await rig.Vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.Empty(rig.Launcher.Launched);
        Assert.Equal(0, rig.Exit.ExitCalls);
        Assert.Equal(UpdatesViewModel.HashMismatchText, rig.Vm.StatusText);
        Assert.Empty(rig.TempFolders());
    }

    [Fact]
    public async Task Update_now_cannot_start_while_busy_or_without_an_update()
    {
        var gate = new TaskCompletionSource();
        using var rig = new UpdatesRig(handler: _ => new StubHttpHandler(async (_, _) =>
        {
            await gate.Task;
            return StubHttpHandler.Status(System.Net.HttpStatusCode.NotFound);
        }));
        Assert.False(rig.Vm.UpdateNowCommand.CanExecute(null));
        await rig.CheckAsync();

        var running = rig.Vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.True(rig.Vm.IsBusy);
        Assert.False(rig.Vm.UpdateNowCommand.CanExecute(null));
        Assert.True(rig.Vm.CancelUpdateCommand.CanExecute(null));
        gate.SetResult();
        await running;
        Assert.False(rig.Vm.IsBusy);
        Assert.Equal(UpdatesViewModel.DownloadFailedText, rig.Vm.StatusText);
    }

    [Fact]
    public async Task Cancel_stops_the_download_and_says_so()
    {
        using var rig = new UpdatesRig(handler: _ => new StubHttpHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return StubHttpHandler.Status(System.Net.HttpStatusCode.NotFound);
        }));
        await rig.CheckAsync();

        var running = rig.Vm.UpdateNowCommand.ExecuteAsync(null);
        rig.Vm.CancelUpdateCommand.Execute(null);
        await running;

        Assert.Equal(UpdatesViewModel.CancelledText, rig.Vm.StatusText);
        Assert.Empty(rig.Launcher.Launched);
        Assert.Empty(rig.TempFolders());
    }

    [Fact]
    public async Task An_exception_while_downloading_is_said_not_thrown_and_leaves_no_folder()
    {
        using var rig = new UpdatesRig(handler: _ => new StubHttpHandler((_, _) => throw new NotSupportedException("boom")));
        await rig.CheckAsync();

        await rig.Vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(UpdatesViewModel.DownloadFailedText, rig.Vm.StatusText);
        Assert.False(rig.Vm.IsBusy);
        Assert.Equal(0, rig.Exit.ExitCalls);
        Assert.Empty(rig.TempFolders());
    }

    [Fact]
    public async Task An_exception_from_the_launcher_is_said_not_thrown_and_deletes_the_download()
    {
        using var rig = new UpdatesRig();
        rig.Launcher.Throw = true;
        await rig.CheckAsync();

        await rig.Vm.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(UpdatesViewModel.DownloadFailedText, rig.Vm.StatusText);
        Assert.False(rig.Vm.IsBusy);
        Assert.Equal(0, rig.Exit.ExitCalls);
        Assert.Empty(rig.TempFolders());
    }

    [Fact]
    public async Task An_exception_while_downloading_the_plugin_is_said_not_thrown()
    {
        using var rig = new UpdatesRig(handler: _ => new StubHttpHandler((_, _) => throw new NotSupportedException("boom")));
        await rig.CheckAsync();

        await rig.Vm.DownloadPluginCommand.ExecuteAsync(null);

        Assert.Equal(UpdatesViewModel.PluginFailedText, rig.Vm.PluginStatusText);
        Assert.False(rig.Vm.IsPluginBusy);
        Assert.Empty(rig.TempFolders());
    }

    [Fact]
    public async Task Release_notes_opens_the_tag_page_of_the_offered_version()
    {
        using var rig = new UpdatesRig();
        await rig.CheckAsync();

        rig.Vm.ReleaseNotesCommand.Execute(null);

        Assert.Equal(["https://github.com/agvmdsa/SkuaLink/releases/tag/v0.8.0"], rig.ReleasePage.Opened);
    }

    [Fact]
    public async Task A_release_page_that_will_not_open_is_said_quietly()
    {
        using var rig = new UpdatesRig();
        rig.ReleasePage.Result = false;
        await rig.CheckAsync();

        rig.Vm.ReleaseNotesCommand.Execute(null);

        Assert.Equal(UpdatesViewModel.ReleasePageFailedText, rig.Vm.StatusText);
    }

    [Fact]
    public async Task Before_updating_the_new_dll_is_required_with_the_version_a_connected_skua_reports()
    {
        using var rig = new UpdatesRig();
        await rig.CheckAsync();

        Assert.True(rig.Vm.HasPluginRequirement);
        Assert.Equal("New DLL required: SkuaLink.SkuaBridge.dll v0.8.0 (matches SkuaLink v0.8.0)", rig.Vm.PluginRequirementText);

        rig.ConnectSkua("0.7.0");

        Assert.Equal(
            "New DLL required: SkuaLink.SkuaBridge.dll v0.8.0 (matches SkuaLink v0.8.0). Connected plugin: v0.7.0.", rig.Vm.PluginRequirementText);
    }

    [Fact]
    public async Task Without_an_update_a_plugin_that_differs_from_the_app_is_required_to_match_it()
    {
        using var rig = new UpdatesRig(released: "0.7.0");
        await rig.CheckAsync();
        Assert.False(rig.Vm.HasPluginRequirement);

        rig.ConnectSkua("0.6.0");

        Assert.Equal(
            "New DLL required: SkuaLink.SkuaBridge.dll v0.7.0 (matches SkuaLink v0.7.0). Required: v0.7.0; installed: v0.6.0.", rig.Vm.PluginRequirementText);

        rig.ConnectSkua("0.7.0");

        Assert.False(rig.Vm.HasPluginRequirement);
    }

    [Fact]
    public async Task Download_plugin_installs_the_dll_of_the_offered_release_and_says_to_reopen_skua()
    {
        using var rig = new UpdatesRig();
        await rig.CheckAsync();

        await rig.Vm.DownloadPluginCommand.ExecuteAsync(null);

        Assert.Equal(UpdatesViewModel.PluginUpdatedText, rig.Vm.PluginStatusText);
        Assert.Equal(rig.PluginBytes, await File.ReadAllBytesAsync(Path.Combine(rig.Plugins, "SkuaLink.SkuaBridge.dll")));
        Assert.Empty(rig.Releases.ByVersionAsked);
        Assert.False(rig.Vm.IsPluginBusy);
    }

    [Fact]
    public async Task Download_plugin_for_a_mismatch_fetches_the_release_tagged_with_the_app_version()
    {
        using var rig = new UpdatesRig(released: "0.7.0");
        await rig.CheckAsync();
        rig.ConnectSkua("0.6.0");

        await rig.Vm.DownloadPluginCommand.ExecuteAsync(null);

        Assert.Equal([new ReleaseVersion(0, 7, 0)], rig.Releases.ByVersionAsked);
        Assert.Equal(UpdatesViewModel.PluginUpdatedText, rig.Vm.PluginStatusText);
        Assert.True(File.Exists(Path.Combine(rig.Plugins, "SkuaLink.SkuaBridge.dll")));
    }

    [Fact]
    public async Task Download_plugin_says_close_skua_when_the_dll_is_held_and_downloads_nothing()
    {
        using var rig = new UpdatesRig();
        var target = Path.Combine(rig.Plugins, "SkuaLink.SkuaBridge.dll");
        await File.WriteAllBytesAsync(target, [1, 2, 3]);
        await rig.CheckAsync();
        await using var held = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await rig.Vm.DownloadPluginCommand.ExecuteAsync(null);

        Assert.Equal("Close Skua and press the button again.", rig.Vm.PluginStatusText);
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Download_plugin_reports_a_missing_release_a_bad_checksum_and_a_missing_folder()
    {
        using var missing = new UpdatesRig(released: "0.7.0");
        await missing.CheckAsync();
        missing.ConnectSkua("0.6.0");
        missing.Releases.ByVersion = _ => new ReleaseCheck(ReleaseCheckStatus.Failed);
        await missing.Vm.DownloadPluginCommand.ExecuteAsync(null);
        Assert.Equal(UpdatesViewModel.PluginMissingText, missing.Vm.PluginStatusText);

        var wrong = $"{new string('0', 64)}  SkuaLink.SkuaBridge.dll\n";
        using var bad = new UpdatesRig(sums: wrong);
        await bad.CheckAsync();
        await bad.Vm.DownloadPluginCommand.ExecuteAsync(null);
        Assert.Equal(UpdatesViewModel.PluginHashText, bad.Vm.PluginStatusText);
        Assert.Empty(Directory.GetFiles(bad.Plugins));

        using var noFolder = new UpdatesRig();
        Directory.Delete(noFolder.Plugins);
        await noFolder.CheckAsync();
        await noFolder.Vm.DownloadPluginCommand.ExecuteAsync(null);
        Assert.Equal(AqwCompanion.App.Settings.SettingsViewModel.SkuaFolderNotFoundText, noFolder.Vm.PluginStatusText);
        Assert.False(Directory.Exists(noFolder.Plugins));
    }

    [Fact]
    public async Task Dispose_stops_following_the_checker()
    {
        using var rig = new UpdatesRig();
        rig.Vm.Dispose();

        await rig.CheckAsync();

        Assert.False(rig.Vm.IsUpdateAvailable);
    }
}
