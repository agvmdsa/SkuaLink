using AqwCompanion.App.Updates;

namespace AqwCompanion.Tests.Updates;

public class UpdateUrlPolicyTests
{
    private static Uri? Parse(string text) => Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out var uri) ? uri : null;

    [Theory]
    [InlineData("https://github.com/agvmdsa/SkuaLink/releases/download/v0.8.0/SHA256SUMS.txt")]
    [InlineData("https://github.com:443/agvmdsa/SkuaLink/releases/download/v0.8.0/x.exe")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1?x=y")]
    [InlineData("https://release-assets.githubusercontent.com/a/b")]
    [InlineData("https://OBJECTS.GITHUBUSERCONTENT.COM/a")]
    public void These_download_addresses_are_allowed(string url)
    {
        Assert.True(UpdateUrlPolicy.IsAllowedDownload(Parse(url)));
    }

    [Theory]
    [InlineData("http://github.com/agvmdsa/SkuaLink/x.exe")]
    [InlineData("https://github.com:8443/x")]
    [InlineData("https://user:pw@github.com/x")]
    [InlineData("https://user@github.com/x")]
    [InlineData("https://evilgithub.com/x")]
    [InlineData("https://github.com.evil.com/x")]
    [InlineData("https://githubusercontent.com/x")]
    [InlineData("https://notgithubusercontent.com/x")]
    [InlineData("https://objects.githubusercontent.com.evil.com/x")]
    [InlineData("https://api.github.com/repos/agvmdsa/SkuaLink/releases/latest")]
    [InlineData("https://github.com./x")]
    [InlineData("ftp://github.com/x")]
    [InlineData("file:///C:/x.exe")]
    [InlineData("/relative/path")]
    [InlineData("github.com/x")]
    public void Every_other_download_address_is_refused(string url)
    {
        Assert.False(UpdateUrlPolicy.IsAllowedDownload(Parse(url)));
    }

    [Fact]
    public void A_null_address_is_refused()
    {
        Assert.False(UpdateUrlPolicy.IsAllowedDownload(null));
        Assert.False(UpdateUrlPolicy.IsAllowedApi(null));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/agvmdsa/SkuaLink/releases/latest", true)]
    [InlineData("https://api.github.com:443/repos/x", true)]
    [InlineData("http://api.github.com/repos/x", false)]
    [InlineData("https://api.github.com:444/repos/x", false)]
    [InlineData("https://api.github.com.evil.com/x", false)]
    [InlineData("https://github.com/agvmdsa/SkuaLink", false)]
    [InlineData("https://user@api.github.com/x", false)]
    public void The_api_is_only_api_github_com(string url, bool allowed)
    {
        Assert.Equal(allowed, UpdateUrlPolicy.IsAllowedApi(Parse(url)));
    }

    [Fact]
    public void At_most_five_redirects_are_followed()
    {
        Assert.Equal(5, UpdateUrlPolicy.MaxRedirects);
    }
}
