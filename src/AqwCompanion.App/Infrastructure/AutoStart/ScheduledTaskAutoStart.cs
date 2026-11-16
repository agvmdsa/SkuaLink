namespace AqwCompanion.App.Infrastructure.AutoStart;

/// <summary>
/// Starts the app at sign-in through a Windows scheduled task (kept from the versions that needed administrator rights, so an
/// existing task can be recreated in place). The task starts the app with <c>--background</c> so it waits in the notification
/// area. <c>/F</c> overwrites a task of an older version, which ran with the highest privileges.
/// </summary>
public sealed class ScheduledTaskAutoStart(IProcessRunner runner, string? exePath, string? userName, Func<bool> isWindows) : IAutoStart
{
    public const string TaskName = "SkuaLink";

    private const string Schtasks = "schtasks.exe";

    public bool IsSupported => isWindows() && !string.IsNullOrWhiteSpace(exePath) && !string.IsNullOrWhiteSpace(userName);

    public bool IsEnabled() => IsSupported && runner.Run(Schtasks, ["/Query", "/TN", TaskName]) == 0;

    public bool TrySet(bool enabled)
    {
        if (!IsSupported)
        {
            return false;
        }

        if (enabled)
        {
            return runner.Run(
                Schtasks,
                [
                    "/Create", "/TN", TaskName, "/TR", "\"" + exePath + "\" " + StartupArguments.BackgroundOption, "/SC", "ONLOGON",
                    "/RL", "LIMITED", "/RU", userName!, "/IT", "/DELAY", "0000:15", "/F",
                ]) == 0;
        }

        // Nothing to remove is the same as removed.
        return !IsEnabled() || runner.Run(Schtasks, ["/Delete", "/TN", TaskName, "/F"]) == 0;
    }
}
