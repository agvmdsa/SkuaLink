namespace AqwCompanion.App.Infrastructure;

/// <summary>The command line the app understands: the background start of the sign-in task.</summary>
public static class StartupArguments
{
    /// <summary>Starts the app in the notification area without opening its window, as the sign-in task does.</summary>
    public const string BackgroundOption = "--background";

    public static bool IsBackground(string[] args) => args.Contains(BackgroundOption, StringComparer.Ordinal);
}
