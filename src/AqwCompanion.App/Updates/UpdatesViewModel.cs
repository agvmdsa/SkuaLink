using System.Globalization;
using AqwCompanion.App.Settings;
using AqwCompanion.App.Shell;
using AqwCompanion.App.Skua;
using AqwCompanion.Phone.Skua;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AqwCompanion.App.Updates;

/// <summary>The "Updates" block of Settings, the update banner and the "New DLL required" part. Nothing starts without a command.</summary>
public sealed partial class UpdatesViewModel : ObservableObject, IDisposable
{
    public const string TitleText = "Updates";
    public const string CheckSwitchTitleText = "Check for updates";
    public const string CheckSwitchDescriptionText = "Asks GitHub for the latest SkuaLink release 30 seconds after the app starts and every 6 hours. Nothing is downloaded or installed until you press Update now.";
    public const string CheckNowText = "Check now";
    public const string UpdateNowText = "Update now";
    public const string ReleaseNotesText = "Release notes";
    public const string DismissText = "Dismiss";
    public const string CancelText = "Cancel";
    public const string DownloadPluginText = "Download plugin";
    public const string NewDllRequiredText = "New DLL required";
    public const string UpdateStartedText = "The installer is starting. SkuaLink will close.";
    public const string UacCancelledText = "The update did not start because the Windows prompt was cancelled. SkuaLink keeps running.";
    public const string LaunchFailedText = "Windows could not start the installer. SkuaLink keeps running.";
    public const string DownloadFailedText = "Could not download the update. Check the connection and try again.";
    public const string TooLargeText = "The download is larger than the release says. It was stopped and nothing was started.";
    public const string HashMismatchText = "The downloaded installer did not match its checksum. It was deleted and nothing was started.";
    public const string HashMissingText = "The release has no usable checksum for the installer. Nothing was started.";
    public const string RefusedText = "The download address was not accepted. Nothing was started.";
    public const string TimedOutText = "The download took too long and was stopped.";
    public const string CancelledText = "The download was cancelled.";
    public const string DiskText = "Could not save the installer on this computer.";
    public const string PluginUpdatedText = "Plugin updated. Reopen Skua.";
    public const string PluginLockedText = "Close Skua and press the button again.";
    public const string PluginMissingText = "The plugin of that release could not be found. Use the release page.";
    public const string PluginFailedText = "Could not download the plugin. Try again, or use the release page.";
    public const string PluginHashText = "The downloaded plugin did not match its checksum. Nothing was changed.";
    public const string ReleasePageFailedText = "Could not open the release page.";

    private readonly SettingsViewModel _settings;
    private readonly UpdateChecker _checker;
    private readonly IGitHubReleases _releases;
    private readonly InstallerDownloader _installer;
    private readonly IInstallerLauncher _launcher;
    private readonly PluginInstaller _plugins;
    private readonly IReleasePageLauncher _releasePage;
    private readonly IApplicationExit _exit;
    private readonly ISkuaFeed? _skua;
    private readonly string _appVersion;
    private readonly Action<Action> _onUiThread;
    private CancellationTokenSource? _download;
    private string _availableVersion = string.Empty;
    private string _dismissedVersion = string.Empty;
    private PluginRequirement? _requirement;
    private bool _disposed;

    public UpdatesViewModel(
        SettingsViewModel settings, UpdateChecker checker, IGitHubReleases releases, InstallerDownloader installer, IInstallerLauncher launcher,
        PluginInstaller plugins, IReleasePageLauncher releasePage, IApplicationExit exit, ISkuaFeed? skua, string appVersion, Action<Action> onUiThread)
    {
        _settings = settings;
        _checker = checker;
        _releases = releases;
        _installer = installer;
        _launcher = launcher;
        _plugins = plugins;
        _releasePage = releasePage;
        _exit = exit;
        _skua = skua;
        _appVersion = appVersion;
        _onUiThread = onUiThread;
        _settings.AttachUpdates(this);
        _settings.CheckForUpdatesChanged += OnSwitchChanged;
        _checker.Changed += OnChanged;
        if (_skua is not null)
        {
            _skua.Changed += OnChanged;
        }

        Refresh();
    }

    /// <summary>The "Check for updates" switch; the setting lives in <see cref="SettingsViewModel"/>.</summary>
    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdatesEnabled;
        set => _settings.CheckForUpdatesEnabled = value;
    }

    [ObservableProperty]
    private string _currentVersionText = string.Empty;

    [ObservableProperty]
    private string _latestVersionText = string.Empty;

    [ObservableProperty]
    private string _lastCheckText = string.Empty;

    [ObservableProperty]
    private string _bannerText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    private string _progressText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelUpdateCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand))]
    [NotifyPropertyChangedFor(nameof(IsBannerVisible))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPluginRequirement))]
    [NotifyCanExecuteChangedFor(nameof(DownloadPluginCommand))]
    private string _pluginRequirementText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadPluginCommand))]
    private bool _isPluginBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPluginStatus))]
    private string _pluginStatusText = string.Empty;

    public bool IsBannerVisible => IsUpdateAvailable && _dismissedVersion != _availableVersion;

    public bool HasStatus => StatusText.Length > 0;

    public bool HasProgress => ProgressText.Length > 0;

    public bool HasPluginRequirement => PluginRequirementText.Length > 0;

    public bool HasPluginStatus => PluginStatusText.Length > 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.CheckForUpdatesChanged -= OnSwitchChanged;
        _checker.Changed -= OnChanged;
        if (_skua is not null)
        {
            _skua.Changed -= OnChanged;
        }
    }

    [RelayCommand]
    private Task CheckNowAsync() => _checker.CheckNowAsync();

    [RelayCommand]
    private void DismissBanner()
    {
        _dismissedVersion = _availableVersion;
        OnPropertyChanged(nameof(IsBannerVisible));
    }

    [RelayCommand]
    private void ReleaseNotes()
    {
        if (_checker.Current.Available is { } release && !_releasePage.Open(ReleasePage.ForVersion(release.Version)))
        {
            StatusText = ReleasePageFailedText;
        }
    }

    private bool CanUpdateNow() => IsUpdateAvailable && !IsBusy;

    /// <summary>
    /// Downloads the installer, checks it, then starts it through the Windows administrator prompt and exits SkuaLink. Any failure keeps
    /// SkuaLink running, deletes the download and starts nothing.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdateNow))]
    private async Task UpdateNowAsync()
    {
        var release = _checker.Current.Available;
        if (release is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = string.Empty;
        using var cts = new CancellationTokenSource();
        _download = cts;
        try
        {
            var progress = new SyncProgress(p => _onUiThread(() => ProgressText = ProgressLine(p)));
            var result = await _installer.DownloadAsync(release, progress, cts.Token);
            if (result.Installer is not { } installer)
            {
                StatusText = FailureText(result.Failure);
                return;
            }

            switch (_launcher.Launch(installer))
            {
                case LaunchStatus.Started:
                    StatusText = UpdateStartedText;
                    _exit.Exit();
                    break;
                case LaunchStatus.Cancelled:
                    TempWorkFolder.Delete(installer.Folder);
                    StatusText = UacCancelledText;
                    break;
                default:
                    TempWorkFolder.Delete(installer.Folder);
                    StatusText = LaunchFailedText;
                    break;
            }
        }
        finally
        {
            _download = null;
            ProgressText = string.Empty;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelUpdate()
    {
        try
        {
            _download?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The download had just finished.
        }
    }

    private bool CanDownloadPlugin() => HasPluginRequirement && !IsPluginBusy;

    /// <summary>Downloads the plugin of the release it must match and puts it in Skua's plugins folder, unless Skua holds the file.</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadPlugin))]
    private async Task DownloadPluginAsync()
    {
        var need = _requirement;
        if (need is null || IsPluginBusy)
        {
            return;
        }

        IsPluginBusy = true;
        PluginStatusText = string.Empty;
        try
        {
            ReleaseInfo? release;
            if (need.Reason == PluginRequirementReason.UpdateAvailable)
            {
                release = _checker.Current.Available;
            }
            else
            {
                var found = await _releases.GetByVersionAsync(need.Required, CancellationToken.None);
                release = found.Status == ReleaseCheckStatus.Found ? found.Release : null;
            }

            if (release is null || release.Version != need.Required)
            {
                PluginStatusText = PluginMissingText;
                return;
            }

            var result = await _plugins.InstallAsync(release, null, CancellationToken.None);
            PluginStatusText = result.Status switch
            {
                PluginInstallStatus.Installed => PluginUpdatedText,
                PluginInstallStatus.Locked => PluginLockedText,
                PluginInstallStatus.FolderMissing => SettingsViewModel.SkuaFolderNotFoundText,
                _ => result.Failure is UpdateFailure.HashMismatch or UpdateFailure.HashMissing ? PluginHashText : PluginFailedText,
            };
        }
        finally
        {
            IsPluginBusy = false;
        }
    }

    private void OnSwitchChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CheckForUpdates));
        _ = _checker.ReconfigureAsync();
    }

    // The checker and the Skua feed raise from any thread.
    private void OnChanged(object? sender, EventArgs e) => _onUiThread(() =>
    {
        if (!_disposed)
        {
            Refresh();
        }
    });

    private void Refresh()
    {
        var state = _checker.Current;
        CurrentVersionText = "Current version " + SettingsViewModel.DisplayVersion(_appVersion);
        LatestVersionText = state.Latest is { } latest ? $"Latest version {latest}" : "Latest version unknown";
        LastCheckText = LastCheck(state);
        _availableVersion = state.Available?.Version.ToString() ?? string.Empty;
        BannerText = state.Available is { } offer ? $"SkuaLink {offer.Version} is available" : string.Empty;
        IsUpdateAvailable = state.IsAvailable;
        OnPropertyChanged(nameof(IsBannerVisible));
        var plugins = _skua is null
            ? []
            : _skua.Current.Instances.Where(i => !i.Disconnected && i.Hello is not null).Select(i => i.Hello!.PluginVersion).ToList();
        _requirement = PluginRule.Evaluate(_appVersion, state.Available?.Version, plugins);
        PluginRequirementText = _requirement is null ? string.Empty : RequirementText(_requirement);
    }

    private static string LastCheck(UpdateState state)
    {
        var time = state.LastCheckUtc?.ToLocalTime().ToString("t", CultureInfo.CurrentCulture) ?? string.Empty;
        return state.Status switch
        {
            UpdateStatus.Off => "Checking for updates is off.",
            UpdateStatus.NeverChecked => "Not checked yet.",
            UpdateStatus.Checking => "Checking...",
            UpdateStatus.CouldNotCheck => $"Could not check at {time}. Trying again later.",
            UpdateStatus.Available => $"Last check {time}: update available.",
            _ => $"Last check {time}: up to date.",
        };
    }

    private static string RequirementText(PluginRequirement need)
    {
        var text = $"{NewDllRequiredText}: SkuaLink.SkuaBridge.dll v{need.Required} (matches SkuaLink v{need.Required})";
        return need.Reason == PluginRequirementReason.UpdateAvailable
            ? text + (need.Installed is null ? string.Empty : $". Connected plugin: v{need.Installed}.")
            : text + $". Required: v{need.Required}; installed: v{need.Installed}.";
    }

    private static string ProgressLine(DownloadProgress p) =>
        p.Total > 0 ? $"Downloading... {p.Received * 100 / p.Total}%" : "Downloading...";

    private static string FailureText(UpdateFailure failure) => failure switch
    {
        UpdateFailure.Refused => RefusedText,
        UpdateFailure.TooLarge => TooLargeText,
        UpdateFailure.TimedOut => TimedOutText,
        UpdateFailure.Cancelled => CancelledText,
        UpdateFailure.HashMissing => HashMissingText,
        UpdateFailure.HashMismatch => HashMismatchText,
        UpdateFailure.Disk => DiskText,
        _ => DownloadFailedText,
    };

    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
