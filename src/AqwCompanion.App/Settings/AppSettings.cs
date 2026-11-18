namespace AqwCompanion.App.Settings;

/// <summary>The player's preferences. They are not data about the player, so Forgetting keeps them.</summary>
/// <param name="AutoStartMigrated">True once a start-at-sign-in task of a version before 0.6 was recreated without the highest
/// privileges (or there was none); missing in older files, which means not done yet.</param>
/// <param name="CheckForUpdates">Whether SkuaLink asks GitHub for new releases (default on); off means no request at all.</param>
/// <param name="AnnouncedUpdate">The release version already announced to the phones (empty = none), so a restart does not announce it again.</param>
public sealed record AppSettings(
    bool DebugMode = false, bool LocalFeed = false, bool PhoneAccess = false, bool AutoStartMigrated = false,
    bool CheckForUpdates = true, string AnnouncedUpdate = "");

/// <summary>Keeps <see cref="AppSettings"/> between runs. Never throws.</summary>
public interface IAppSettingsStore
{
    /// <summary>The saved settings, or the defaults when there is no usable file.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
