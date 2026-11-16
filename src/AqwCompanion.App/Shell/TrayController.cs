using AqwCompanion.App.Debug;
using AqwCompanion.App.Feed;
using AqwCompanion.Phone.Skua;

namespace AqwCompanion.App.Shell;

/// <summary>
/// The decisions behind the tray icon: closing the window hides it; the menu opens the window, toggles debug mode and
/// really exits. It knows no Avalonia types.
/// </summary>
public sealed class TrayController
{
    private const string Name = "SkuaLink";

    private readonly IMainWindowHost _window;
    private readonly IApplicationExit _exit;
    private readonly DebugModeController? _debug;
    private readonly ISkuaFeed? _skua;
    private int _exiting;

    public TrayController(IMainWindowHost window, IApplicationExit exit, DebugModeController? debug = null, ISkuaFeed? skua = null)
    {
        _window = window;
        _exit = exit;
        _debug = debug;
        _skua = skua;
        if (_skua is not null)
        {
            _skua.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The Skua connections changed (the feed's event, re-raised; may come from any thread).</summary>
    public event EventHandler? Changed;

    /// <summary>True once Exit started; from then on a close is never turned into a hide.</summary>
    public bool IsExiting => Volatile.Read(ref _exiting) != 0;

    /// <summary>
    /// <paramref name="userInitiated"/> is true when the user pressed the close button, false for an OS shutdown, an
    /// application shutdown or an owner closing. Returns true when the close must be cancelled because the window was hidden instead.
    /// </summary>
    public bool ShouldCancelClose(bool userInitiated)
    {
        if (!userInitiated || IsExiting)
        {
            return false;
        }

        _window.Hide();
        return true;
    }

    /// <summary>Shows the window.</summary>
    public void Open() => _window.Show();

    /// <summary>Opens or closes the debug window; does nothing when the app wired no debug mode.</summary>
    public void ToggleDebug() => _debug?.Toggle();

    /// <summary>Exits the application. Safe to call twice: only the first call exits.</summary>
    public Task ExitAsync()
    {
        if (Interlocked.Exchange(ref _exiting, 1) == 0)
        {
            _exit.Exit();
        }

        return Task.CompletedTask;
    }

    /// <summary>The tray tooltip: the product name and how many Skua are connected.</summary>
    public string ToolTip =>
        Name + " - " + FeedSnapshotBuilder.SkuaStateText(_skua is null ? 0 : FeedSnapshotBuilder.ConnectedCount(_skua.Current)).TrimEnd('.');
}
