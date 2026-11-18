using AqwCompanion.App.Updates;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Updates;

public class PluginInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"));
    private readonly string _plugins;
    private readonly string _temp;
    private readonly byte[] _newDll = Enumerable.Range(0, 3000).Select(i => (byte)(i % 13)).ToArray();

    public PluginInstallerTests()
    {
        _plugins = Path.Combine(_root, "plugins");
        _temp = Path.Combine(_root, "temp");
        Directory.CreateDirectory(_plugins);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Target => Path.Combine(_plugins, "SkuaLink.SkuaBridge.dll");

    private PluginInstaller Installer(StubHttpHandler handler) => new(new GuardedDownloader(new HttpClient(handler)), _temp, _plugins);

    private string[] InPlugins() => Directory.GetFileSystemEntries(_plugins).Select(Path.GetFileName).Order().ToArray()!;

    [Fact]
    public async Task A_good_download_creates_the_dll_and_writes_nothing_else_in_the_plugins_folder()
    {
        var (release, handler) = TestReleases.Served("0.8.0", [1], _newDll);

        var result = await Installer(handler).InstallAsync(release, null, default);

        Assert.Equal(new PluginInstallResult(PluginInstallStatus.Installed), result);
        Assert.Equal(_newDll, await File.ReadAllBytesAsync(Target));
        Assert.Equal(["SkuaLink.SkuaBridge.dll"], InPlugins());
    }

    [Fact]
    public async Task An_older_dll_is_replaced()
    {
        await File.WriteAllBytesAsync(Target, [7, 7, 7]);
        var (release, handler) = TestReleases.Served("0.8.0", [1], _newDll);

        var result = await Installer(handler).InstallAsync(release, null, default);

        Assert.Equal(PluginInstallStatus.Installed, result.Status);
        Assert.Equal(_newDll, await File.ReadAllBytesAsync(Target));
        Assert.Equal(["SkuaLink.SkuaBridge.dll"], InPlugins());
    }

    [Fact]
    public async Task A_dll_that_Skua_holds_open_is_not_touched_and_nothing_is_downloaded()
    {
        await File.WriteAllBytesAsync(Target, [7, 7, 7]);
        var (release, handler) = TestReleases.Served("0.8.0", [1], _newDll);
        await using var held = new FileStream(Target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await Installer(handler).InstallAsync(release, null, default);

        Assert.Equal(PluginInstallStatus.Locked, result.Status);
        Assert.Empty(handler.Requests);
        held.Close();
        Assert.Equal(new byte[] { 7, 7, 7 }, await File.ReadAllBytesAsync(Target));
        Assert.Equal(["SkuaLink.SkuaBridge.dll"], InPlugins());
    }

    [Fact]
    public async Task A_missing_plugins_folder_is_reported_and_never_created()
    {
        var (release, handler) = TestReleases.Served("0.8.0", [1], _newDll);
        var missing = Path.Combine(_root, "nope");
        var installer = new PluginInstaller(new GuardedDownloader(new HttpClient(handler)), _temp, missing);

        var result = await installer.InstallAsync(release, null, default);

        Assert.Equal(PluginInstallStatus.FolderMissing, result.Status);
        Assert.False(Directory.Exists(missing));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_hash_mismatch_leaves_the_old_dll_and_no_other_file()
    {
        await File.WriteAllBytesAsync(Target, [7, 7, 7]);
        var sums = $"{new string('0', 64)}  SkuaLink.SkuaBridge.dll\n";
        var (release, handler) = TestReleases.Served("0.8.0", [1], _newDll, sums);

        var result = await Installer(handler).InstallAsync(release, null, default);

        Assert.Equal(new PluginInstallResult(PluginInstallStatus.Failed, UpdateFailure.HashMismatch), result);
        Assert.Equal(new byte[] { 7, 7, 7 }, await File.ReadAllBytesAsync(Target));
        Assert.Equal(["SkuaLink.SkuaBridge.dll"], InPlugins());
    }

    [Fact]
    public async Task A_download_failure_is_a_failure_and_the_temp_folder_is_removed()
    {
        var (release, _) = TestReleases.Served("0.8.0", [1], _newDll);
        var handler = new StubHttpHandler(_ => StubHttpHandler.Status(System.Net.HttpStatusCode.NotFound));

        var result = await Installer(handler).InstallAsync(release, null, default);

        Assert.Equal(new PluginInstallResult(PluginInstallStatus.Failed, UpdateFailure.Download), result);
        Assert.Empty(InPlugins());
        Assert.Empty(Directory.Exists(_temp) ? Directory.GetDirectories(_temp) : []);
    }

    [Fact]
    public async Task After_a_success_the_temp_folder_is_removed_too()
    {
        var (release, handler) = TestReleases.Served("0.8.0", [1], _newDll);

        await Installer(handler).InstallAsync(release, null, default);

        Assert.Empty(Directory.GetDirectories(_temp));
    }

    [Fact]
    public void The_temp_name_is_not_a_dll_so_Skua_never_loads_it()
    {
        Assert.EndsWith(".tmp", "SkuaLink.SkuaBridge.dll" + PluginInstaller.TempSuffix, StringComparison.Ordinal);
        Assert.False(("SkuaLink.SkuaBridge.dll" + PluginInstaller.TempSuffix).EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    }
}
