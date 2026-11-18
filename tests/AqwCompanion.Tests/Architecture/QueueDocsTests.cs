namespace AqwCompanion.Tests.Architecture;

/// <summary>The script queue must be described honestly in the README: its limits, what it cannot tell and what it stores.</summary>
public class QueueDocsTests
{
    private static string Readme() => File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));

    /// <summary>Only the part of the README between the "## Script queue" heading and the next "## " heading.</summary>
    private static string QueueSection()
    {
        var readme = Readme();
        var start = readme.IndexOf("## Script queue", StringComparison.Ordinal);
        Assert.True(start >= 0, "README has no '## Script queue' heading");
        var next = readme.IndexOf("\n## ", start + 3, StringComparison.Ordinal);
        return next < 0 ? readme[start..] : readme[start..next];
    }

    [Theory]
    [InlineData("up to 20 steps")]
    [InlineData("inventory only")]
    [InlineData("stops the script when the goal is reached")]
    [InlineData("ended without the item")]
    [InlineData("Problems pause the queue")]
    [InlineData("Nothing is retried")]
    [InlineData("Skip step")]
    [InlineData("Stop queue")]
    [InlineData("reputation rank")]
    [InlineData("skua-queue.<character>.json")]
    [InlineData("Forget my data")]
    [InlineData("switch them on once")]
    [InlineData("cannot tell")]
    [InlineData("Pause or Stop queue in SkuaLink before")]
    [InlineData("never stops a script it did not start")]
    [InlineData("overwrites the whole list")]
    [InlineData("refuses to save an empty queue")]
    // The honesty phrases: what has not been verified, and what the owner must still know.
    [InlineData("has not been run against a real Skua")]
    [InlineData("may be a daily you already did today")]
    [InlineData("Nothing starts when SkuaLink opens")]
    [InlineData("the phone editor does not show it and does not remove one the PC set")]
    [InlineData("**The queue needs you** and **The queue finished**, have their own switches")]
    [InlineData("there is no migration")]
    public void The_readme_describes_the_script_queue_its_limits_and_its_privacy(string phrase)
    {
        Assert.Contains(phrase, QueueSection(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_queue_section_is_a_slice_of_its_own()
    {
        var section = QueueSection();

        Assert.StartsWith("## Script queue", section, StringComparison.Ordinal);
        Assert.DoesNotContain("\n## ", section, StringComparison.Ordinal);
        Assert.DoesNotContain("## Using the PC app", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plugin_protocol_messages_and_commands_are_exactly_the_ones_that_existed_before_the_queue()
    {
        var assembly = typeof(AqwCompanion.Skua.SkuaMessage).Assembly;
        var messages = assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(AqwCompanion.Skua.SkuaMessage))).Select(t => t.Name).Order(StringComparer.Ordinal).ToArray();
        var commands = assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(AqwCompanion.Skua.SkuaCommand))).Select(t => t.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            ["GetOptionsCommand", "HelloMessage", "InventoryMessage", "ListScriptsCommand", "LogMessage", "ResultMessage", "SetOptionsCommand",
             "SkuaCommand", "StartCommand", "StatusMessage", "StopCommand", "UnsupportedVersionMessage"],
            messages);
        Assert.Equal(["GetOptionsCommand", "ListScriptsCommand", "SetOptionsCommand", "StartCommand", "StopCommand"], commands);
    }

    [Fact]
    public void The_plugin_and_the_protocol_project_do_not_mention_the_queue()
    {
        foreach (var folder in new[] { "plugin", Path.Combine("src", "AqwCompanion.Skua") })
        {
            var files = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), folder), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
            foreach (var file in files)
            {
                // A secondary guard: the plugin and the protocol already use generic .NET queues (a command channel, a pending
                // list), so only terms of the script queue feature are searched.
                var text = File.ReadAllText(file);
                foreach (var term in new[] { "ScriptQueue", "QueueStep", "QueueEngine", "QueueAttention", "QueueFinished", "skua-queue" })
                {
                    Assert.DoesNotContain(term, text, StringComparison.Ordinal);
                }
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
