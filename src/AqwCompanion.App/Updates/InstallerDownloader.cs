namespace AqwCompanion.App.Updates;

/// <summary>An installer SkuaLink downloaded itself and checked against the release's SHA256SUMS.txt. Only <see cref="InstallerDownloader"/> makes one.</summary>
public sealed class VerifiedInstaller
{
    internal VerifiedInstaller(string folder, string path)
    {
        Folder = folder;
        Path = path;
    }

    public string Folder { get; }

    public string Path { get; }
}

public sealed record InstallerDownload(VerifiedInstaller? Installer, UpdateFailure Failure);

public sealed class InstallerDownloader(GuardedDownloader downloader, string tempRoot)
{
    /// <summary>Downloads and verifies the installer of <paramref name="release"/>; on any failure the work folder is deleted and nothing is returned to run.</summary>
    public async Task<InstallerDownload> DownloadAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        string folder;
        try
        {
            folder = TempWorkFolder.Create(tempRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new InstallerDownload(null, UpdateFailure.Disk);
        }

        UpdateFailure failure;
        try
        {
            failure = await VerifiedFetch.RunAsync(downloader, folder, release.Sums, release.Installer, progress, ct);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TempWorkFolder.Delete(folder);
            throw;
        }

        if (failure != UpdateFailure.None)
        {
            TempWorkFolder.Delete(folder);
            return new InstallerDownload(null, failure);
        }

        return new InstallerDownload(new VerifiedInstaller(folder, Path.Combine(folder, release.Installer.Name)), UpdateFailure.None);
    }
}
