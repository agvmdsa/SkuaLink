namespace AqwCompanion.Tests.Architecture;

/// <summary>
/// The installer cannot be built or run here (Inno Setup is Windows-only and runs in the release workflow), so these tests guard
/// the rules it must keep: Npcap is never mentioned, the app starts as the signed-in user after setup, and the files it names are
/// in the repository.
/// </summary>
public class InstallerScriptTests
{
    private static string Script() => File.ReadAllText(Path.Combine(RepositoryRoot(), "installer", "SkuaLink.iss"));

    [Fact]
    public void The_installer_never_mentions_npcap_and_has_no_custom_page()
    {
        var script = Script();
        Assert.DoesNotContain("npcap", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreateCustomPage", script);
    }

    [Fact]
    public void The_installer_asks_for_administrator_rights_to_install_into_program_files()
    {
        Assert.Contains("PrivilegesRequired=admin", Script());
    }

    [Fact]
    public void The_start_after_install_entry_runs_as_the_signed_in_user()
    {
        var launch = Script().Split('\n').Single(line => line.Contains("Description: \"Start ", StringComparison.Ordinal));
        Assert.Contains("postinstall", launch);
        Assert.Contains("runasoriginaluser", launch);
        Assert.DoesNotContain("shellexec", launch);
    }

    [Fact]
    public void The_installer_removes_the_start_with_windows_task_and_never_deletes_data_without_asking()
    {
        var script = Script();

        Assert.Contains("schtasks.exe", script);
        Assert.Contains("MsgBox", script);
        Assert.Contains("UninstallSilent", script);
    }

    [Fact]
    public void The_installer_icon_and_the_data_folder_name_match_the_app()
    {
        var script = Script();

        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "src", "AqwCompanion.App", "Assets", "app.ico")));
        Assert.Contains(@"SetupIconFile=..\src\AqwCompanion.App\Assets\app.ico", script);
        Assert.Contains("#define MyDataFolder \"AqwCompanion\"", script);
    }

    [Fact]
    public void The_release_workflow_builds_the_installer_and_lists_it_in_the_checksums()
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "release.yml"));

        Assert.Contains("installer/SkuaLink.iss", workflow);
        Assert.Contains("SkuaLink-Setup-v", workflow);
        Assert.Contains("SHA256SUMS.txt", workflow);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "installer", "SkuaLink.iss")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("installer/SkuaLink.iss not found above " + AppContext.BaseDirectory);
    }
}
