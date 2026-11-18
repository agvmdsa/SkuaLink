using AqwCompanion.App.Shell;
using AqwCompanion.App.Updates;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Updates;

public class TrayUpdateTests
{
    private sealed class FakeUpdateState : IUpdateState
    {
        public UpdateState Current { get; set; } = UpdateState.Initial;

        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void The_tray_reports_an_available_update_and_re_raises_changes()
    {
        var updates = new FakeUpdateState();
        var controller = new TrayController(new FakeMainWindowHost(), new FakeApplicationExit(), updates: updates);
        var raised = 0;
        controller.Changed += (_, _) => raised++;
        Assert.False(controller.UpdateAvailable);

        updates.Current = new UpdateState(UpdateStatus.Available, TestReleases.Info("0.8.0"), new ReleaseVersion(0, 8, 0), DateTimeOffset.UnixEpoch);
        updates.Raise();

        Assert.True(controller.UpdateAvailable);
        Assert.Equal(1, raised);
        Assert.Equal("Update available", TrayController.UpdateAvailableText);
    }

    [Fact]
    public void A_tray_without_an_update_state_never_reports_an_update()
    {
        var controller = new TrayController(new FakeMainWindowHost(), new FakeApplicationExit());

        Assert.False(controller.UpdateAvailable);
    }
}
