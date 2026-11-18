using System.Diagnostics;
using AqwCompanion.App.Skua;
using AqwCompanion.App.Updates;

namespace AqwCompanion.Tests.Updates;

public class ReleasePageLauncherTests
{
    [Fact]
    public void The_tag_page_address_is_built_from_the_version()
    {
        Assert.Equal("https://github.com/agvmdsa/SkuaLink/releases/tag/v1.2.3", ReleasePage.ForVersion(new ReleaseVersion(1, 2, 3)));
    }

    [Theory]
    [InlineData("https://github.com/agvmdsa/SkuaLink/releases/latest", true)]
    [InlineData("https://github.com/agvmdsa/SkuaLink/releases/tag/v0.8.0", true)]
    [InlineData("https://github.com/agvmdsa/SkuaLink/releases/tag/v0.8.0\n", false)]
    [InlineData("https://github.com/agvmdsa/SkuaLink/releases/tag/v0.8", false)]
    [InlineData("https://github.com/agvmdsa/SkuaLink/releases/tag/v0.8.0-beta", false)]
    [InlineData("https://github.com/agvmdsa/SkuaLink/releases/tag/v0.8.0/../../x", false)]
    [InlineData("https://github.com/evil/SkuaLink/releases/tag/v0.8.0", false)]
    [InlineData("http://github.com/agvmdsa/SkuaLink/releases/latest", false)]
    [InlineData("https://evil.example/agvmdsa/SkuaLink/releases/latest", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_release_pages_of_this_repository_are_allowed(string? url, bool allowed)
    {
        Assert.Equal(allowed, ReleasePage.IsAllowed(url));
    }

    [Fact]
    public void The_launcher_hands_an_allowed_address_to_the_system_and_nothing_else()
    {
        var started = new List<string>();
        var launcher = new ProcessReleasePageLauncher(info => { started.Add(info.FileName); return true; });

        Assert.True(launcher.Open("https://github.com/agvmdsa/SkuaLink/releases/tag/v0.8.0"));
        Assert.True(launcher.Open(ReleasePage.Url));
        Assert.False(launcher.Open("https://evil.example/"));

        Assert.Equal(["https://github.com/agvmdsa/SkuaLink/releases/tag/v0.8.0", ReleasePage.Url], started);
    }
}
