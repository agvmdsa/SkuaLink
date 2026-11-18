namespace AqwCompanion.Tests.Architecture;

/// <summary>0.8.0 checks GitHub for updates and can install them: the README must say what it contacts, what it verifies and what it cannot promise.</summary>
public class UpdateDocsTests
{
    private static string Readme() => File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));

    [Theory]
    [InlineData("Check for updates")]
    [InlineData("GitHub")]
    [InlineData("every 6 hours")]
    [InlineData("30 seconds")]
    [InlineData("Update now")]
    [InlineData("Windows administrator prompt")]
    [InlineData("SHA-256")]
    [InlineData("SHA256SUMS.txt")]
    [InlineData("not code-signed")]
    [InlineData("compromised release")]
    [InlineData("New DLL required")]
    [InlineData("Download plugin")]
    [InlineData("Close Skua and press the button again")]
    [InlineData("Release notes")]
    [InlineData("subscribed before the update check existed must switch the new **update** notification on once")]
    public void The_readme_states_what_the_update_check_contacts_verifies_and_cannot_promise(string phrase)
    {
        Assert.Contains(phrase, Readme(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_nothing_leaves_the_pc_line_names_the_update_check_as_well_as_notifications()
    {
        var line = Readme().Split('\n').Single(l => l.Contains("Nothing leaves the PC", StringComparison.Ordinal));

        Assert.Contains("Notifications", line, StringComparison.Ordinal);
        Assert.Contains("GitHub", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_still_describes_the_manual_plugin_install()
    {
        Assert.Contains("Copy it into `%AppData%\\Skua\\plugins`", Readme(), StringComparison.Ordinal);
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
