using AqwCompanion.App.Shell;
using AqwCompanion.Phone.Skua;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Shell;

public class TrayControllerTests
{
    private sealed class RecordingExit(List<string> log) : IApplicationExit
    {
        public int Calls { get; private set; }

        public void Exit()
        {
            Calls++;
            log.Add("exit");
        }
    }

    [Fact]
    public void Toggling_debug_from_the_tray_flips_the_debug_mode()
    {
        var settings = new AqwCompanion.App.Settings.SettingsViewModel(
            new FakeAppSettingsStore(), new AqwCompanion.Core.Forgetting.ForgetCoordinator(() => true, () => []));
        var host = new FakeDebugWindowHost();
        var setup = new Setup();
        var controller = new TrayController(
            setup.Window, setup.Exit, debug: new AqwCompanion.App.Debug.DebugModeController(settings, host));

        controller.ToggleDebug();
        Assert.True(host.IsOpen);

        controller.ToggleDebug();
        Assert.False(host.IsOpen);
    }

    [Fact]
    public void Toggling_debug_without_a_debug_mode_does_nothing()
    {
        var setup = new Setup();

        var ex = Record.Exception(setup.Controller.ToggleDebug);

        Assert.Null(ex);
    }

    private sealed class Setup
    {
        public Setup()
        {
            Exit = new RecordingExit(Log);
            Window = new FakeMainWindowHost(Log);
            Controller = new TrayController(Window, Exit);
        }

        public List<string> Log { get; } = [];

        public RecordingExit Exit { get; }

        public FakeMainWindowHost Window { get; }

        public TrayController Controller { get; }
    }

    [Fact]
    public void A_close_by_the_user_hides_the_window_and_is_cancelled()
    {
        var setup = new Setup();

        var cancel = setup.Controller.ShouldCancelClose(userInitiated: true);

        Assert.True(cancel);
        Assert.Equal(1, setup.Window.HideCalls);
    }

    [Fact]
    public void A_close_that_is_not_from_the_user_goes_through_and_does_not_hide()
    {
        var setup = new Setup();

        var cancel = setup.Controller.ShouldCancelClose(userInitiated: false);

        Assert.False(cancel);
        Assert.Equal(0, setup.Window.HideCalls);
    }

    [Fact]
    public async Task After_exit_started_no_close_is_cancelled()
    {
        var setup = new Setup();

        await setup.Controller.ExitAsync();

        Assert.True(setup.Controller.IsExiting);
        Assert.False(setup.Controller.ShouldCancelClose(userInitiated: true));
        Assert.Equal(0, setup.Window.HideCalls);
    }

    [Fact]
    public async Task Exit_is_idempotent()
    {
        var setup = new Setup();

        await setup.Controller.ExitAsync();
        await setup.Controller.ExitAsync();

        Assert.Equal(1, setup.Exit.Calls);
    }

    [Fact]
    public async Task Exit_exits_once()
    {
        var exit = new FakeApplicationExit();
        var tray = new TrayController(new FakeMainWindowHost(), exit);
        await tray.ExitAsync();
        await tray.ExitAsync();
        Assert.Equal(1, exit.ExitCalls);
        Assert.True(tray.IsExiting);
    }

    [Fact]
    public void Open_shows_the_window()
    {
        var setup = new Setup();

        setup.Controller.Open();

        Assert.Equal(1, setup.Window.ShowCalls);
    }

    [Fact]
    public void The_tooltip_says_how_many_skua_are_connected()
    {
        var skua = new FakeSkua();
        var tray = new TrayController(new FakeMainWindowHost(), new FakeApplicationExit(), skua: skua);
        Assert.Equal("SkuaLink - Skua not connected", tray.ToolTip);
        skua.Current = new SkuaHubView([FakeSkua.Instance("a", "Hero")]);
        Assert.Equal("SkuaLink - 1 Skua connected", tray.ToolTip);
    }

    [Fact]
    public void Without_a_skua_feed_the_tooltip_says_skua_is_not_connected()
    {
        var setup = new Setup();

        Assert.Equal("SkuaLink - Skua not connected", setup.Controller.ToolTip);
    }

    [Fact]
    public void Changed_is_raised_when_skua_changes()
    {
        var skua = new FakeSkua();
        var tray = new TrayController(new FakeMainWindowHost(), new FakeApplicationExit(), skua: skua);
        var raised = 0;
        tray.Changed += (_, _) => raised++;

        skua.Raise();

        Assert.Equal(1, raised);
    }
}
