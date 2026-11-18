using AqwCompanion.App.Settings;
using AqwCompanion.App.Skua;
using AqwCompanion.App.Updates;
using AqwCompanion.Core.Forgetting;
using AqwCompanion.Phone.Notices;
using AqwCompanion.Tests.TestDoubles;

namespace AqwCompanion.Tests.Updates;

public sealed class FakeInstallerLauncher : IInstallerLauncher
{
    public LaunchStatus Result { get; set; } = LaunchStatus.Started;

    public bool Throw { get; set; }

    public List<VerifiedInstaller> Launched { get; } = [];

    public bool InstallerExistedAtLaunch { get; private set; }

    public LaunchStatus Launch(VerifiedInstaller installer)
    {
        Launched.Add(installer);
        InstallerExistedAtLaunch = File.Exists(installer.Path);
        if (Throw)
        {
            throw new InvalidOperationException("boom");
        }

        return Result;
    }
}

public sealed class FakeReleasePage : IReleasePageLauncher
{
    public bool Result { get; set; } = true;

    public List<string> Opened { get; } = [];

    public bool Open(string url)
    {
        Opened.Add(url);
        return Result;
    }
}

/// <summary>An <see cref="UpdatesViewModel"/> over fakes: one release (assets served by a stub handler), a temp plugins folder, a fake Skua feed.</summary>
public sealed class UpdatesRig : IDisposable
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    public UpdatesRig(
        string app = "0.7.0", string released = "0.8.0", string? sums = null, SettingsViewModel? settings = null,
        Action<Action>? onUiThread = null, Func<StubHttpHandler, StubHttpHandler>? handler = null)
    {
        Directory.CreateDirectory(Plugins);
        Settings = settings ?? new SettingsViewModel(new FakeAppSettingsStore(), new ForgetCoordinator(() => true, () => []));
        var (release, served) = TestReleases.Served(released, InstallerBytes, PluginBytes, sums);
        Release = release;
        Handler = handler?.Invoke(served) ?? served;
        Releases.Default = new ReleaseCheck(ReleaseCheckStatus.Found, release, "\"e\"");
        Releases.ByVersion = _ => Releases.Default;
        Checker = new UpdateChecker(
            Releases, app, () => Settings.CheckForUpdatesEnabled, new FakeDelay(), () => T0, () => Settings.AnnouncedUpdate,
            Settings.RememberAnnouncedUpdate, Notices.Add, Errors.Add);
        var downloader = new GuardedDownloader(new HttpClient(Handler));
        Vm = new UpdatesViewModel(
            Settings, Checker, Releases, new InstallerDownloader(downloader, TempRoot), Launcher, new PluginInstaller(downloader, TempRoot, Plugins),
            ReleasePage, Exit, Skua, app, onUiThread ?? (action => action()));
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "aqw-tests", Guid.NewGuid().ToString("N"));

    public string Plugins => Path.Combine(Root, "plugins");

    public string TempRoot => Path.Combine(Root, "temp");

    public byte[] InstallerBytes { get; } = Enumerable.Range(0, 20_000).Select(i => (byte)(i % 11)).ToArray();

    public byte[] PluginBytes { get; } = Enumerable.Range(0, 4_000).Select(i => (byte)(i % 17)).ToArray();

    public SettingsViewModel Settings { get; }

    public FakeGitHubReleases Releases { get; } = new();

    public StubHttpHandler Handler { get; }

    public ReleaseInfo Release { get; }

    public UpdateChecker Checker { get; }

    public FakeSkua Skua { get; } = new();

    public FakeApplicationExit Exit { get; } = new();

    public FakeInstallerLauncher Launcher { get; } = new();

    public FakeReleasePage ReleasePage { get; } = new();

    public List<Notice> Notices { get; } = [];

    public List<Exception> Errors { get; } = [];

    public UpdatesViewModel Vm { get; }

    public Task CheckAsync() => Checker.CheckNowAsync();

    /// <summary>A connected Skua whose plugin reports this version.</summary>
    public void ConnectSkua(string pluginVersion)
    {
        var instance = FakeSkua.Instance("a", "Hero") with { Hello = new AqwCompanion.Skua.SkuaHello(pluginVersion, "1.4.5.0", "a") };
        Skua.Current = new AqwCompanion.Phone.Skua.SkuaHubView([instance]);
        Skua.Raise();
    }

    public string[] TempFolders() => Directory.Exists(TempRoot) ? Directory.GetDirectories(TempRoot) : [];

    public void Dispose()
    {
        Vm.Dispose();
        Checker.Dispose();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
