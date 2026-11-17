namespace AqwCompanion.Tests.Architecture;

/// <summary>0.7.0 ships push notifications: the README must say what they need, what they send and what they cannot do.</summary>
public class PushDocsTests
{
    private static string Readme() => File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));

    [Fact]
    public void The_readme_no_longer_says_push_notifications_do_not_exist()
    {
        Assert.DoesNotContain("do not exist yet", Readme(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Notifications")]
    [InlineData("Home Screen")]
    [InlineData("iOS 16.4")]
    [InlineData("while SkuaLink is closed")]
    [InlineData("internet")]
    [InlineData("Apple")]
    [InlineData("Google")]
    [InlineData("encrypted")]
    [InlineData("5 deaths in 2 minutes")]
    [InlineData("10 minutes")]
    [InlineData("only while a script runs")]
    [InlineData("vapid.key")]
    [InlineData("push.json")]
    public void The_readme_states_the_requirement_privacy_and_limit(string phrase)
    {
        Assert.Contains(phrase, Readme(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_does_not_claim_nothing_leaves_the_pc_without_naming_notifications()
    {
        var text = Readme();
        var line = text.Split('\n').Single(l => l.Contains("Nothing leaves the PC", StringComparison.Ordinal));
        Assert.Contains("Notifications", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_has_no_capture_words()
    {
        Assert.DoesNotContain("npcap", Readme(), StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AqwCompanion.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("AqwCompanion.slnx not found above " + AppContext.BaseDirectory);
    }
}
