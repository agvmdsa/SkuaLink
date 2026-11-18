using AqwCompanion.App.Characters;
using AqwCompanion.App.Debug;
using AqwCompanion.App.Diagnostics;
using AqwCompanion.App.HardFarms;
using AqwCompanion.App.Queue;
using AqwCompanion.App.Settings;
using CommunityToolkit.Mvvm.Input;

namespace AqwCompanion.App.Shell;

public sealed partial class MainViewModel(
    DiagnosticsViewModel diagnostics,
    HardFarmsViewModel hardFarms,
    CharactersViewModel characters,
    SettingsViewModel settings,
    ISettingsDialog settingsDialog,
    DebugModeController debug,
    QueueViewModel? queue = null)
{
    public string Title { get; } = AppInfo.ProductName;
    public DiagnosticsViewModel Diagnostics { get; } = diagnostics;
    public HardFarmsViewModel HardFarms { get; } = hardFarms;
    public CharactersViewModel Characters { get; } = characters;
    public SettingsViewModel Settings { get; } = settings;
    public DebugModeController Debug { get; } = debug;

    /// <summary>The script queue card; null when the app was built without it (the card is then hidden).</summary>
    public QueueViewModel? Queue { get; } = queue;

    /// <summary>Opens the Settings dialog over the window.</summary>
    [RelayCommand]
    private void ShowSettings() => settingsDialog.Show(Settings);

    /// <summary>Opens or closes the debug window (Ctrl+Shift+D).</summary>
    [RelayCommand]
    private void ToggleDebug() => Debug.Toggle();
}
