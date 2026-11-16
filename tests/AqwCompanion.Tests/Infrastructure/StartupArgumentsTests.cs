using AqwCompanion.App.Infrastructure;

namespace AqwCompanion.Tests.Infrastructure;

public class StartupArgumentsTests
{
    [Fact]
    public void The_background_option_is_recognised_only_when_present()
    {
        Assert.True(StartupArguments.IsBackground(["--background"]));
        Assert.True(StartupArguments.IsBackground(["--other", "--background"]));
        Assert.False(StartupArguments.IsBackground([]));
        Assert.False(StartupArguments.IsBackground(["--background=1"]));
    }
}
