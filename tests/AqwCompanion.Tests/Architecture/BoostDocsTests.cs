using AqwCompanion.App.Settings;

namespace AqwCompanion.Tests.Architecture;

/// <summary>The boosts and "log in again" must be described honestly in the README, and no text may still say nothing reaches the game.</summary>
public class BoostDocsTests
{
    private static string Readme() => File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));

    /// <summary>Only the part of the README between the "## Boosts and log in again" heading and the next "## " heading.</summary>
    private static string BoostSection()
    {
        var readme = Readme();
        var start = readme.IndexOf("## Boosts and log in again", StringComparison.Ordinal);
        Assert.True(start >= 0, "README has no '## Boosts and log in again' heading");
        var next = readme.IndexOf("\n## ", start + 3, StringComparison.Ordinal);
        return next < 0 ? readme[start..] : readme[start..next];
    }

    [Fact]
    public void The_boost_section_is_a_slice_of_its_own()
    {
        var section = BoostSection();

        Assert.StartsWith("## Boosts and log in again", section, StringComparison.Ordinal);
        Assert.DoesNotContain("\n## ", section, StringComparison.Ordinal);
        Assert.DoesNotContain("## Script queue", section, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("four boost types")]
    [InlineData("gold, class, reputation and XP")]
    [InlineData("remaining time")]
    [InlineData("pauses while the character is logged out")]
    [InlineData("shows \"active\" without a time")]
    [InlineData("asks first")]
    [InlineData("only when you tap")]
    [InlineData("attempt per tap")]
    [InlineData("at most one every 15 seconds")]
    [InlineData("never automatic")]
    [InlineData("never sees or stores a password")]
    [InlineData("credentials Skua already holds")]
    [InlineData("cannot start Skua")]
    [InlineData("Boost ended")]
    [InlineData("switch it on once")]
    [InlineData("there is no migration")]
    [InlineData("stays paused")]
    [InlineData("Continue")]
    [InlineData("memory only")]
    [InlineData("Forget my data")]
    [InlineData("Update the plugin DLL")]
    [InlineData("same release")]
    [InlineData("has not been run against a real Skua")]
    [InlineData("never from the queue")]
    [InlineData("does not use or equip anything else")]
    [InlineData("the phone's clock")]
    [InlineData("without a Use button")]
    [InlineData("seconds left as of the last sync")]
    [InlineData("stay the same within a session")]
    [InlineData("the only boost notification")]
    [InlineData("about 6 to 9 seconds after a boost expires")]
    [InlineData("up to 3 seconds too long")]
    [InlineData("at least 5 seconds after the first")]
    public void The_readme_describes_the_boosts_their_limits_and_their_privacy(string phrase)
    {
        Assert.Contains(phrase, BoostSection(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_no_longer_claims_it_sends_nothing_to_the_game()
    {
        var readme = Readme();

        Assert.DoesNotContain("exactly five things", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("it sends nothing to the game", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("It never sends anything to the game.", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("never sends anything to the game", readme, StringComparison.Ordinal);

        const string except = "except the boost you use and the log in you ask for";
        var count = 0;
        for (var at = readme.IndexOf(except, StringComparison.Ordinal); at >= 0; at = readme.IndexOf(except, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        Assert.Equal(3, count);
    }

    [Fact]
    public void The_settings_texts_name_the_boosts()
    {
        Assert.Contains("boost", SettingsViewModel.PrivacyText, StringComparison.Ordinal);
        Assert.Contains("only when you tap", SettingsViewModel.PrivacyText, StringComparison.Ordinal);
        Assert.DoesNotContain("never sends anything to the game", SettingsViewModel.PrivacyText, StringComparison.Ordinal);
        Assert.Contains("use a boost", SettingsViewModel.PhoneDescriptionText, StringComparison.Ordinal);
        Assert.Contains("log in again", SettingsViewModel.PhoneDescriptionText, StringComparison.Ordinal);
        Assert.DoesNotContain("never sends anything to the game", SettingsViewModel.PhoneDescriptionText, StringComparison.Ordinal);
        Assert.Contains("use a boost and log in again", SettingsViewModel.SkuaDescriptionText, StringComparison.Ordinal);
        Assert.Equal(
            "Remove everything the app saved on this computer? Your saved characters, Skua logs, script queues and the Hard Farms list are cleared, the boost readings held in memory are dropped, and so is the game data saved by versions before 0.6.",
            SettingsViewModel.ForgetPromptText);
    }

    [Fact]
    public void The_plugin_readme_lists_the_two_new_commands_and_the_members_it_uses()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "plugin", "SkuaLink.SkuaBridge", "README.md"));

        foreach (var phrase in new[] { "useBoost", "relogin", "Boosts.UseBoost", "Servers.Relogin", "GetGameObject<int>", "iBoostG", "never calls `GetBoostID`" })
        {
            Assert.Contains(phrase, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_protocol_gained_exactly_the_two_commands()
    {
        var assembly = typeof(AqwCompanion.Skua.SkuaMessage).Assembly;
        var commands = assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(AqwCompanion.Skua.SkuaCommand))).Select(t => t.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(["GetOptionsCommand", "ListScriptsCommand", "ReloginCommand", "SetOptionsCommand", "StartCommand", "StopCommand", "UseBoostCommand"], commands);
    }

    [Fact]
    public void The_boost_code_does_not_mention_the_queue_and_the_queue_does_not_mention_boosts()
    {
        var root = RepositoryRoot();
        foreach (var folder in new[] { Path.Combine("src", "AqwCompanion.Phone", "Queue"), Path.Combine("src", "AqwCompanion.App", "Queue") })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, folder), "*.*", SearchOption.AllDirectories))
            {
                Assert.DoesNotContain("Boost", File.ReadAllText(file), StringComparison.Ordinal);
            }
        }

        var boostFiles = new[] { Path.Combine("src", "AqwCompanion.Phone", "Boosts"), Path.Combine("src", "AqwCompanion.App", "Boosts") }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "AqwCompanion.Skua"), "*Boost*.cs"))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "plugin", "SkuaLink.SkuaBridge"), "*Boost*.cs"));
        foreach (var file in boostFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var term in new[] { "QueueEngine", "QueueStep", "skua-queue" })
            {
                Assert.DoesNotContain(term, text, StringComparison.Ordinal);
            }
        }
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
