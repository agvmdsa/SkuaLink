using AqwCompanion.App;

namespace AqwCompanion.Tests.Infrastructure;

public class AppPathsTests
{
    [Fact]
    public void The_root_is_the_app_folder_in_local_app_data()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AqwTest"),
            AppPaths.ForCurrentUser("AqwTest").Root);
    }

    [Fact]
    public void The_push_files_are_in_the_root()
    {
        var paths = new AppPaths("root");

        Assert.Equal(Path.Combine("root", "push.json"), paths.PushPath);
        Assert.Equal(Path.Combine("root", "vapid.key"), paths.VapidKeyPath);
    }
}
