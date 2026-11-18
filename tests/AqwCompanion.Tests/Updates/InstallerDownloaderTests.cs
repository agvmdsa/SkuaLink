using AqwCompanion.App.Updates;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Updates;

public class InstallerDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _installer = Enumerable.Range(0, 50_000).Select(i => (byte)(i % 7)).ToArray();
    private readonly byte[] _plugin = [1, 2, 3, 4];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private InstallerDownloader Downloader(StubHttpHandler handler) => new(new GuardedDownloader(new HttpClient(handler)), _root);

    private string[] Folders() => Directory.Exists(_root) ? Directory.GetDirectories(_root) : [];

    [Fact]
    public async Task A_good_download_leaves_the_verified_installer_in_a_new_folder_under_the_root()
    {
        var (release, handler) = TestReleases.Served("0.8.0", _installer, _plugin);

        var result = await Downloader(handler).DownloadAsync(release, null, default);

        Assert.Equal(UpdateFailure.None, result.Failure);
        var installer = result.Installer!;
        Assert.Equal(Path.Combine(installer.Folder, "SkuaLink-Setup-v0.8.0.exe"), installer.Path);
        Assert.StartsWith(Path.Combine(_root, "SkuaLink-update-"), installer.Folder, StringComparison.Ordinal);
        Assert.Equal(_installer, await File.ReadAllBytesAsync(installer.Path));
        Assert.True(File.Exists(Path.Combine(installer.Folder, "SHA256SUMS.txt")));
        Assert.Equal(release.Sums.Url, handler.Requests[0].Uri);
    }

    [Fact]
    public async Task Each_download_gets_its_own_new_folder()
    {
        var (release, handler) = TestReleases.Served("0.8.0", _installer, _plugin);
        var downloader = Downloader(handler);

        var a = await downloader.DownloadAsync(release, null, default);
        var b = await downloader.DownloadAsync(release, null, default);

        Assert.NotEqual(a.Installer!.Folder, b.Installer!.Folder);
    }

    [Fact]
    public async Task A_hash_mismatch_deletes_the_folder_and_returns_no_installer()
    {
        var wrongSums = $"{new string('0', 64)}  SkuaLink-Setup-v0.8.0.exe\n";
        var (release, handler) = TestReleases.Served("0.8.0", _installer, _plugin, wrongSums);

        var result = await Downloader(handler).DownloadAsync(release, null, default);

        Assert.Null(result.Installer);
        Assert.Equal(UpdateFailure.HashMismatch, result.Failure);
        Assert.Empty(Folders());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{0}  SkuaLink.SkuaBridge.dll\n")]
    [InlineData("{0}  other/SkuaLink-Setup-v0.8.0.exe\n")]
    [InlineData("nothex  SkuaLink-Setup-v0.8.0.exe\n")]
    public async Task A_missing_or_malformed_checksum_line_fails_and_deletes_the_folder(string template)
    {
        var sums = string.Format(System.Globalization.CultureInfo.InvariantCulture, template, TestReleases.Hex(_installer));
        var (release, handler) = TestReleases.Served("0.8.0", _installer, _plugin, sums);

        var result = await Downloader(handler).DownloadAsync(release, null, default);

        Assert.Null(result.Installer);
        Assert.Equal(UpdateFailure.HashMissing, result.Failure);
        Assert.Empty(Folders());
    }

    [Fact]
    public async Task The_installer_is_not_downloaded_when_the_sums_cannot_be_read()
    {
        var (release, handler) = TestReleases.Served("0.8.0", _installer, _plugin, sums: "");

        await Downloader(handler).DownloadAsync(release, null, default);

        Assert.Single(handler.Requests);
        Assert.EndsWith("SHA256SUMS.txt", handler.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_download_failure_maps_to_a_failure_and_deletes_the_folder()
    {
        var (release, _) = TestReleases.Served("0.8.0", _installer, _plugin);
        var handler = new StubHttpHandler(_ => StubHttpHandler.Status(System.Net.HttpStatusCode.NotFound));

        var result = await Downloader(handler).DownloadAsync(release, null, default);

        Assert.Equal(UpdateFailure.Download, result.Failure);
        Assert.Empty(Folders());
    }

    [Fact]
    public async Task A_size_bigger_than_the_release_declared_is_too_large()
    {
        var (release, handler) = TestReleases.Served("0.8.0", _installer, _plugin);
        var lying = release with { Installer = release.Installer with { Size = _installer.Length - 1 } };

        var result = await Downloader(handler).DownloadAsync(lying, null, default);

        Assert.Equal(UpdateFailure.TooLarge, result.Failure);
        Assert.Empty(Folders());
    }

    [Fact]
    public async Task A_cancel_deletes_the_folder()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHttpHandler(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return StubHttpHandler.Ok("");
        });

        var result = await Downloader(handler).DownloadAsync(TestReleases.Info("0.8.0"), null, cts.Token);

        Assert.Equal(UpdateFailure.Cancelled, result.Failure);
        Assert.Empty(Folders());
    }

    [Fact]
    public async Task A_root_that_cannot_be_used_is_a_disk_failure()
    {
        var file = Path.Combine(Path.GetTempPath(), "aqw-tests-file-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(file, "x");
        try
        {
            var (release, handler) = TestReleases.Served("0.8.0", _installer, _plugin);
            var downloader = new InstallerDownloader(new GuardedDownloader(new HttpClient(handler)), file);

            var result = await downloader.DownloadAsync(release, null, default);

            Assert.Equal(UpdateFailure.Disk, result.Failure);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void The_temp_work_folder_is_new_and_random_and_delete_never_throws()
    {
        var a = TempWorkFolder.Create(_root);
        var b = TempWorkFolder.Create(_root);

        Assert.NotEqual(a, b);
        Assert.Empty(Directory.GetFileSystemEntries(a));
        TempWorkFolder.Delete(a);
        TempWorkFolder.Delete(a);
        TempWorkFolder.Delete(Path.Combine(_root, "missing"));
        Assert.False(Directory.Exists(a));
    }
}
