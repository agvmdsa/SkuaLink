namespace AqwCompanion.App.Settings;

/// <summary>The player's preferences. They are not data about the player, so Forgetting keeps them.</summary>
/// <param name="AutoStartMigrated">True once a start-at-sign-in task of a version before 0.6 was recreated without the highest
/// privileges (or there was none); missing in older files, which means not done yet.</param>
public sealed record AppSettings(bool DebugMode = false, bool LocalFeed = false, bool PhoneAccess = false, bool AutoStartMigrated = false);

/// <summary>Keeps <see cref="AppSettings"/> between runs. Never throws.</summary>
public interface IAppSettingsStore
{
    /// <summary>The saved settings, or the defaults when there is no usable file.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
