using System.ComponentModel;
using System.Diagnostics;
using AqwCompanion.App.Updates;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Updates;

public class InstallerLauncherTests
{
    private static VerifiedInstaller Installer() => new("/tmp/SkuaLink-update-abc", "/tmp/SkuaLink-update-abc/SkuaLink-Setup-v0.8.0.exe");

    [Fact]
    public void The_installer_is_started_through_the_shell_with_the_runas_verb_and_no_arguments()
    {
        ProcessStartInfo? seen = null;
        var launcher = new ProcessInstallerLauncher(info => { seen = info; return true; });

        var status = launcher.Launch(Installer());

        Assert.Equal(LaunchStatus.Started, status);
        Assert.NotNull(seen);
        Assert.Equal(Installer().Path, seen.FileName);
        Assert.True(seen.UseShellExecute);
        Assert.Equal("runas", seen.Verb);
        Assert.Equal(string.Empty, seen.Arguments);
        Assert.Empty(seen.ArgumentList);
        Assert.Equal(Installer().Folder, seen.WorkingDirectory);
    }

    [Fact]
    public void A_cancelled_windows_prompt_is_reported_as_cancelled()
    {
        var launcher = new ProcessInstallerLauncher(_ => throw new Win32Exception(1223, "The operation was canceled by the user"));

        Assert.Equal(LaunchStatus.Cancelled, launcher.Launch(Installer()));
    }

    [Fact]
    public void Any_other_failure_is_reported_as_failed_without_throwing()
    {
        Assert.Equal(LaunchStatus.Failed, new ProcessInstallerLauncher(_ => throw new Win32Exception(5)).Launch(Installer()));
        Assert.Equal(LaunchStatus.Failed, new ProcessInstallerLauncher(_ => throw new InvalidOperationException()).Launch(Installer()));
        Assert.Equal(LaunchStatus.Failed, new ProcessInstallerLauncher(_ => false).Launch(Installer()));
    }

    [Fact]
    public async Task Only_a_downloaded_and_verified_installer_can_be_launched()
    {
        var root = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var installer = new byte[] { 1, 2, 3 };
            var (release, handler) = TestReleases.Served("0.8.0", installer, [9]);
            var download = await new InstallerDownloader(new GuardedDownloader(new HttpClient(handler)), root).DownloadAsync(release, null, default);
            ProcessStartInfo? seen = null;

            new ProcessInstallerLauncher(info => { seen = info; return true; }).Launch(download.Installer!);

            Assert.Equal(download.Installer!.Path, seen!.FileName);
            Assert.Empty(typeof(VerifiedInstaller).GetConstructors());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
