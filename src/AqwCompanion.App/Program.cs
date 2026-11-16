using AqwCompanion.App.Infrastructure;
using Avalonia;
using Avalonia.Threading;
using Serilog;

namespace AqwCompanion.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var instance = new SingleInstance(AppInfo.ExeName);
        if (!instance.TryBecomePrimary(TimeSpan.Zero))
        {
            instance.SignalPrimary();
            return;
        }

        instance.ListenForSignals(() => Dispatcher.UIThread.Post(() => (Application.Current as CompanionApp)?.OpenMainWindow()));

        var paths = AppPaths.ForCurrentUser(AppInfo.ExeName);

        Log.Logger = LoggingSetup.CreateLogger(paths, AppInfo.Version);
        Log.Information("Starting {SafeProduct} {SafeVersion}", AppInfo.ProductName, AppInfo.Version);

        CrashHandler.Install(paths.CrashDirectory, AppInfo.Version);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashHandler.Report(ex, paths.CrashDirectory, AppInfo.Version);
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
            StartSafetyNet();
        }
    }

    // Safety net: if something (a stray foreground thread, a driver handle) keeps the process alive after the window loop
    // has ended, exit for real 5 seconds later. It is a background thread, so a normal exit simply discards it.
    private static void StartSafetyNet() =>
        new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(5));
            Environment.Exit(0);
        })
        { IsBackground = true, Name = "Exit safety net" }.Start();

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<CompanionApp>().UsePlatformDetect().LogToTrace();
}
