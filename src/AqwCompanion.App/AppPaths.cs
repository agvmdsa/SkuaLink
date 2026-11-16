namespace AqwCompanion.App;

public sealed record AppPaths(string Root)
{
    public string LogDirectory => Path.Combine(Root, "logs");
    public string CrashDirectory => Path.Combine(Root, "crashes");
    public string ExportDirectory => Path.Combine(Root, "exports");
    public string DataDirectory => Path.Combine(Root, "data");
    public string ReputationDirectory => Path.Combine(Root, "reputation");
    public string FeedTokenPath => Path.Combine(Root, "feed.token");
    public string SettingsPath => Path.Combine(Root, "settings.json");
    public string PhonePath => Path.Combine(Root, "phone.json");
    public string PushPath => Path.Combine(Root, "push.json");
    public string VapidKeyPath => Path.Combine(Root, "vapid.key");
    public string SkuaLogPath => Path.Combine(Root, "skua-log.json");
    public string SkuaScriptPrefsPath => Path.Combine(Root, "skua-script-prefs.json");
    public string SkuaScriptOptionsPath => Path.Combine(Root, "skua-script-options.json");

    /// <summary>An empty flag file kept while the phone page is published through Tailscale; not character data.</summary>
    public string PhoneServeMarkerPath => Path.Combine(Root, "phone-serve.flag");
    public string DatabasePath => Path.Combine(DataDirectory, "companion.db");

    /// <summary>The per-user root: <c>%LOCALAPPDATA%\&lt;appName&gt;</c>.</summary>
    public static AppPaths ForCurrentUser(string appName) =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName));
}
