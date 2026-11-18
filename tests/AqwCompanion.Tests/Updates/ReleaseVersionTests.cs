using AqwCompanion.App.Updates;

namespace AqwCompanion.Tests.Updates;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v0.8.0", 0, 8, 0)]
    [InlineData("v10.20.30", 10, 20, 30)]
    [InlineData("v123456.0.1", 123456, 0, 1)]
    public void A_release_tag_parses(string tag, int major, int minor, int patch)
    {
        Assert.True(ReleaseVersion.TryParseTag(tag, out var version));
        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
        Assert.Equal(tag, version.Tag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.8.0")]
    [InlineData("V0.8.0")]
    [InlineData("v0.8")]
    [InlineData("v1.2.3.4")]
    [InlineData("v0.8.0-beta.1")]
    [InlineData("v0.8.0\n")]
    [InlineData(" v0.8.0")]
    [InlineData("v-1.0.0")]
    [InlineData("v9999999.0.0")]
    [InlineData("v\u0660.\u0668.\u0660")]
    public void A_malformed_tag_is_refused(string? tag)
    {
        Assert.False(ReleaseVersion.TryParseTag(tag, out _));
    }

    [Theory]
    [InlineData("0.7.0", 0, 7, 0, false)]
    [InlineData("0.7.0+abc123", 0, 7, 0, false)]
    [InlineData("0.8.0-dev.3+abc123", 0, 8, 0, true)]
    [InlineData("1.2.3-rc.1", 1, 2, 3, true)]
    public void An_app_version_parses_without_its_build_metadata(string text, int major, int minor, int patch, bool pre)
    {
        Assert.True(ReleaseVersion.TryParseApp(text, out var version, out var isPreRelease));
        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
        Assert.Equal(pre, isPreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("0.7")]
    [InlineData("v0.7.0")]
    [InlineData("0.7.0-")]
    public void A_malformed_app_version_is_refused(string? text)
    {
        Assert.False(ReleaseVersion.TryParseApp(text, out _, out _));
    }

    [Fact]
    public void Versions_compare_numerically_not_as_text()
    {
        var v = (int a, int b, int c) => new ReleaseVersion(a, b, c);

        Assert.True(v(0, 10, 0) > v(0, 9, 0));
        Assert.True(v(0, 7, 1) > v(0, 7, 0));
        Assert.True(v(1, 0, 0) > v(0, 99, 99));
        Assert.False(v(0, 7, 0) > v(0, 7, 0));
        Assert.True(v(0, 7, 0) >= v(0, 7, 0));
        Assert.True(v(0, 6, 9) < v(0, 7, 0));
        Assert.True(v(0, 7, 0) <= v(0, 7, 0));
        Assert.Equal("0.10.2", v(0, 10, 2).ToString());
    }
}
