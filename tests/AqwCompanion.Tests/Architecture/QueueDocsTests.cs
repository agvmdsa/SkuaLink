namespace AqwCompanion.Tests.Architecture;

/// <summary>The script queue must be described honestly in the README: its limits, what it cannot tell and what it stores.</summary>
public class QueueDocsTests
{
    private static string Readme() => File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));

    [Theory]
    [InlineData("## Script queue")]
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
    public void The_readme_describes_the_script_queue_its_limits_and_its_privacy(string phrase)
    {
        Assert.Contains(phrase, Readme(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_still_lists_exactly_the_five_things_the_plugin_does()
    {
        Assert.Contains("the plugin does exactly five things", Readme(), StringComparison.Ordinal);
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
                // The plugin and the protocol already use generic .NET queues (a command channel, a pending list):
                // what must not appear is anything of the script queue feature.
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
