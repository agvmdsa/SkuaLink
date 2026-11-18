namespace AqwCompanion.App.Updates;

public enum PluginInstallStatus { Installed, Locked, FolderMissing, Failed }

public sealed record PluginInstallResult(PluginInstallStatus Status, UpdateFailure Failure = UpdateFailure.None);

/// <summary>
/// Puts the plugin of a release into Skua's plugins folder: only when the folder exists and the current file is not held open, only after
/// the download matched its checksum, only as the single file name <c>SkuaLink.SkuaBridge.dll</c> (through a transient temp name in the same folder).
/// </summary>
public sealed class PluginInstaller(GuardedDownloader downloader, string tempRoot, string pluginsFolder)
{
    public const string TempSuffix = ".update.tmp";

    public async Task<PluginInstallResult> InstallAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (!Directory.Exists(pluginsFolder))
        {
            return new PluginInstallResult(PluginInstallStatus.FolderMissing);
        }

        var target = Path.Combine(pluginsFolder, ReleaseParser.PluginFileName);
        if (IsLocked(target))
        {
            return new PluginInstallResult(PluginInstallStatus.Locked);
        }

        string folder;
        try
        {
            folder = TempWorkFolder.Create(tempRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PluginInstallResult(PluginInstallStatus.Failed, UpdateFailure.Disk);
        }

        var temp = target + TempSuffix;
        try
        {
            var failure = await VerifiedFetch.RunAsync(downloader, folder, release.Sums, release.Plugin, progress, ct);
            if (failure != UpdateFailure.None)
            {
                return new PluginInstallResult(PluginInstallStatus.Failed, failure);
            }

            File.Copy(Path.Combine(folder, release.Plugin.Name), temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
            return new PluginInstallResult(PluginInstallStatus.Installed);
        }
        catch (IOException)
        {
            // Skua opened the file between the check and the move.
            return new PluginInstallResult(PluginInstallStatus.Locked);
        }
        catch (UnauthorizedAccessException)
        {
            return new PluginInstallResult(PluginInstallStatus.Failed, UpdateFailure.Disk);
        }
        finally
        {
            TryDelete(temp);
            TempWorkFolder.Delete(folder);
        }
    }

    /// <summary>True when the file exists and cannot be opened for writing without sharing (Skua has the plugin loaded).</summary>
    private static bool IsLocked(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next install overwrites it.
        }
    }
}
