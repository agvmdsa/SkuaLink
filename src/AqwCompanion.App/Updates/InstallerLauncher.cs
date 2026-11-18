using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace AqwCompanion.App.Updates;

public enum LaunchStatus { Started, Cancelled, Failed }

/// <summary>Starts a verified installer. Taking a <see cref="VerifiedInstaller"/> (not a path) is the guard: nothing from the network can name the file.</summary>
public interface IInstallerLauncher
{
    /// <summary>Never throws.</summary>
    LaunchStatus Launch(VerifiedInstaller installer);
}

/// <summary>ShellExecute with the <c>runas</c> verb: the normal Windows administrator prompt, no arguments, so never silent.</summary>
public sealed class ProcessInstallerLauncher(Func<ProcessStartInfo, bool>? start = null) : IInstallerLauncher
{
    private const int ErrorCancelledByUser = 1223;

    public LaunchStatus Launch(VerifiedInstaller installer)
    {
        try
        {
            var info = new ProcessStartInfo(installer.Path)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = installer.Folder,
            };
            return (start ?? StartProcess)(info) ? LaunchStatus.Started : LaunchStatus.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelledByUser)
        {
            return LaunchStatus.Cancelled;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Starting the installer failed: {ExceptionType}", ex.GetType().Name);
            return LaunchStatus.Failed;
        }
    }

    // With the shell Process.Start can answer null for a started process; a real failure throws.
    private static bool StartProcess(ProcessStartInfo info)
    {
        using var process = Process.Start(info);
        return true;
    }
}
