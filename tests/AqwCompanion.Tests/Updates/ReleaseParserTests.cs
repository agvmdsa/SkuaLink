using AqwCompanion.App.Updates;

namespace AqwCompanion.Tests.Updates;

public class ReleaseParserTests
{
    private const string Base = "https://github.com/agvmdsa/SkuaLink/releases/download/v0.8.0/";

    private static string Asset(string name, long size, string? url = null)
    {
        var address = url ?? Base + name;
        return $$"""{"name":"{{name}}","size":{{size}},"browser_download_url":"{{address}}"}""";
    }

    private static string Release(string tag, bool draft, bool prerelease, params string[] assets)
    {
        var d = draft ? "true" : "false";
        var p = prerelease ? "true" : "false";
        return $$"""{"tag_name":"{{tag}}","draft":{{d}},"prerelease":{{p}},"assets":[{{string.Join(",", assets)}}]}""";
    }

    private static string[] Good() =>
        [Asset("SkuaLink-Setup-v0.8.0.exe", 50_000_000), Asset("SkuaLink.SkuaBridge.dll", 200_000), Asset("SHA256SUMS.txt", 300)];

    [Fact]
    public void A_complete_release_is_ready_with_its_three_assets()
    {
        var result = ReleaseParser.Parse(Release("v0.8.0", false, false, Good()));

        Assert.Equal(ReleaseParseStatus.Ready, result.Status);
        var release = result.Release!;
        Assert.Equal(new ReleaseVersion(0, 8, 0), release.Version);
        Assert.Equal("SkuaLink-Setup-v0.8.0.exe", release.Installer.Name);
        Assert.Equal(50_000_000, release.Installer.Size);
        Assert.Equal(new Uri(Base + "SkuaLink.SkuaBridge.dll"), release.Plugin.Url);
        Assert.Equal("SHA256SUMS.txt", release.Sums.Name);
    }

    [Fact]
    public void Other_assets_such_as_the_zip_are_ignored()
    {
        var assets = Good().Append(Asset("SkuaLink-v0.8.0-win-x64.zip", 1)).ToArray();

        Assert.Equal(ReleaseParseStatus.Ready, ReleaseParser.Parse(Release("v0.8.0", false, false, assets)).Status);
    }

    [Fact]
    public void A_draft_or_a_prerelease_is_not_ready()
    {
        Assert.Equal(ReleaseParseStatus.NotReady, ReleaseParser.Parse(Release("v0.8.0", true, false, Good())).Status);
        Assert.Equal(ReleaseParseStatus.NotReady, ReleaseParser.Parse(Release("v0.8.0", false, true, Good())).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_release_missing_one_of_the_three_assets_is_not_ready(int missing)
    {
        var assets = Good().Where((_, i) => i != missing).ToArray();

        Assert.Equal(ReleaseParseStatus.NotReady, ReleaseParser.Parse(Release("v0.8.0", false, false, assets)).Status);
    }

    [Fact]
    public void An_installer_named_for_another_version_does_not_count()
    {
        var assets = new[] { Asset("SkuaLink-Setup-v0.8.1.exe", 5), Asset("SkuaLink.SkuaBridge.dll", 5), Asset("SHA256SUMS.txt", 5) };

        Assert.Equal(ReleaseParseStatus.NotReady, ReleaseParser.Parse(Release("v0.8.0", false, false, assets)).Status);
    }

    [Theory]
    [InlineData("https://evil.example/SkuaLink-Setup-v0.8.0.exe")]
    [InlineData("http://github.com/agvmdsa/SkuaLink/releases/download/v0.8.0/SkuaLink-Setup-v0.8.0.exe")]
    [InlineData("https://github.com.evil.com/SkuaLink-Setup-v0.8.0.exe")]
    [InlineData("not a url")]
    public void An_asset_with_a_url_outside_the_policy_makes_the_release_not_ready(string url)
    {
        var assets = new[] { Asset("SkuaLink-Setup-v0.8.0.exe", 5, url), Asset("SkuaLink.SkuaBridge.dll", 5), Asset("SHA256SUMS.txt", 5) };

        Assert.Equal(ReleaseParseStatus.NotReady, ReleaseParser.Parse(Release("v0.8.0", false, false, assets)).Status);
    }

    [Theory]
    [InlineData(300L * 1024 * 1024 + 1, 5, 5)]
    [InlineData(5, 10L * 1024 * 1024 + 1, 5)]
    [InlineData(5, 5, 64L * 1024 + 1)]
    [InlineData(0, 5, 5)]
    [InlineData(5, -1, 5)]
    [InlineData(5, 5, 0)]
    public void An_asset_over_its_cap_or_without_a_positive_size_makes_the_release_not_ready(long installer, long plugin, long sums)
    {
        var assets = new[] { Asset("SkuaLink-Setup-v0.8.0.exe", installer), Asset("SkuaLink.SkuaBridge.dll", plugin), Asset("SHA256SUMS.txt", sums) };

        Assert.Equal(ReleaseParseStatus.NotReady, ReleaseParser.Parse(Release("v0.8.0", false, false, assets)).Status);
    }

    [Fact]
    public void The_caps_are_exactly_allowed()
    {
        var assets = new[]
        {
            Asset("SkuaLink-Setup-v0.8.0.exe", ReleaseParser.MaxInstallerBytes),
            Asset("SkuaLink.SkuaBridge.dll", ReleaseParser.MaxPluginBytes),
            Asset("SHA256SUMS.txt", ReleaseParser.MaxSumsBytes),
        };

        Assert.Equal(ReleaseParseStatus.Ready, ReleaseParser.Parse(Release("v0.8.0", false, false, assets)).Status);
    }

    [Theory]
    [InlineData("v0.8")]
    [InlineData("latest")]
    [InlineData("v0.8.0-beta.1")]
    [InlineData("")]
    public void A_bad_tag_is_invalid(string tag)
    {
        Assert.Equal(ReleaseParseStatus.Invalid, ReleaseParser.Parse(Release(tag, false, false, Good())).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"v0.8.0\"")]
    [InlineData("{\"tag_name\":\"v0.8.0\"}")]
    [InlineData("{\"tag_name\":5,\"draft\":false,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v0.8.0\",\"draft\":\"no\",\"prerelease\":false}")]
    public void Anything_that_is_not_a_release_object_is_invalid(string json)
    {
        Assert.Equal(ReleaseParseStatus.Invalid, ReleaseParser.Parse(json).Status);
    }

    [Fact]
    public void Hostile_nesting_is_invalid_not_a_crash()
    {
        var deep = new string('[', 100) + new string(']', 100);
        var json = $$"""{"tag_name":"v0.8.0","draft":false,"prerelease":false,"assets":[],"x":{{deep}}}""";

        Assert.Equal(ReleaseParseStatus.Invalid, ReleaseParser.Parse(json).Status);
    }

    [Fact]
    public void Odd_entries_in_the_asset_list_are_skipped()
    {
        var json = $$"""{"tag_name":"v0.8.0","draft":false,"prerelease":false,"assets":[1,"x",null,[],{"name":5},{{string.Join(",", Good())}}]}""";

        Assert.Equal(ReleaseParseStatus.Ready, ReleaseParser.Parse(json).Status);
    }

    [Fact]
    public void Assets_that_are_not_a_list_make_the_release_not_ready()
    {
        var json = """{"tag_name":"v0.8.0","draft":false,"prerelease":false,"assets":"none"}""";

        Assert.Equal(ReleaseParseStatus.NotReady, ReleaseParser.Parse(json).Status);
    }

    [Fact]
    public void A_lone_surrogate_in_the_tag_is_invalid_not_a_crash()
    {
        var json = Release("v0.8.0", false, false, Good()).Replace("\"v0.8.0\"", "\"\\ud800\"", StringComparison.Ordinal);

        Assert.Equal(ReleaseParseStatus.Invalid, ReleaseParser.Parse(json).Status);
    }

    [Fact]
    public void A_lone_surrogate_in_an_asset_name_or_url_never_throws_and_is_never_ready()
    {
        var inName = new[] { Asset("\\ud800", 5), Asset("SkuaLink-Setup-v0.8.0.exe", 5), Asset("SkuaLink.SkuaBridge.dll", 5), Asset("SHA256SUMS.txt", 5) };
        var inUrl = new[] { Asset("SkuaLink-Setup-v0.8.0.exe", 5, "https://github.com/\\ud800"), Asset("SkuaLink.SkuaBridge.dll", 5), Asset("SHA256SUMS.txt", 5) };

        Assert.NotEqual(ReleaseParseStatus.Ready, ReleaseParser.Parse(Release("v0.8.0", false, false, inName)).Status);
        Assert.NotEqual(ReleaseParseStatus.Ready, ReleaseParser.Parse(Release("v0.8.0", false, false, inUrl)).Status);
    }

    [Fact]
    public void The_installer_name_follows_the_version()
    {
        Assert.Equal("SkuaLink-Setup-v1.2.3.exe", ReleaseParser.InstallerFileName(new ReleaseVersion(1, 2, 3)));
    }
}
