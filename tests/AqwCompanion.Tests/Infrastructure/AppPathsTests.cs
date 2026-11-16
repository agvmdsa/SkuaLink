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
}
